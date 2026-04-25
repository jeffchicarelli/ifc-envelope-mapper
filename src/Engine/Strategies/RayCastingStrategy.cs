#define DEBUGMESH
#if RELEASE
#undef DEBUGMESH
#endif

using g4;
using IfcEnvelopeMapper.Core.Domain.Element;
using IfcEnvelopeMapper.Core.Domain.Surface;
using IfcEnvelopeMapper.Core.Extensions;
using IfcEnvelopeMapper.Core.Pipeline.Detection;
using Microsoft.Extensions.Logging;

#if DEBUGMESH
using IfcEnvelopeMapper.Engine.Visualization;
#endif

using static IfcEnvelopeMapper.Core.Diagnostics.AppLog;

namespace IfcEnvelopeMapper.Engine.Strategies;

/// <summary>
/// Detects exterior elements by per-triangle ray casting (Ying et al. 2022).
/// </summary>
/// <remarks>
/// Algorithm:
/// <list type="number">
///   <item>Merge all element meshes into one global <c>DMesh3</c> and record triangle ownership.</item>
///   <item>Build a BVH (<see cref="DMeshAABBTree3"/>) over the global mesh.</item>
///   <item>For every triangle of every element, cast <c>N</c> rays from <c>centroid + ε·normal</c>
///         along the outward normal, jittered by ±<c>jitterDeg</c>. A ray "escapes" if it hits
///         nothing or hits a triangle owned by the same element.</item>
///   <item>Triangle is exterior iff <c>escapes / N ≥ hitRatio</c>.</item>
///   <item>Element is exterior iff at least one of its triangles is exterior.</item>
/// </list>
/// </remarks>
public sealed class RayCastingStrategy : IDetectionStrategy
{
    private const double EPSILON = 1e-6;

    private readonly int _numRays;
    private readonly double _jitterRad;
    private readonly double _hitRatio;
    private readonly IFaceExtractor _faceExtractor;

    public RayCastingStrategy(
        int numRays = 8,
        double jitterDeg = 5.0,
        double hitRatio = 0.5,
        IFaceExtractor? faceExtractor = null)
    {
        _numRays = numRays;
        _jitterRad = jitterDeg * Math.PI / 180.0;
        _hitRatio = hitRatio;
        _faceExtractor = faceExtractor ?? new PcaFaceExtractor();
    }

    public DetectionResult Detect(IEnumerable<BuildingElement> elements)
    {
        var elementsList = elements.ToList();
        if (elementsList.Count == 0)
        {
            return EmptyResult();
        }

#if DEBUGMESH
        foreach (var element in elementsList)
        {
            GeometryDebug.Element(element.Mesh, element.GlobalId, element.IfcType,
                IfcTypePalette.For(element.IfcType));
        }
#endif

        var (globalMesh, triToElement) = MergeMeshes(elementsList);
        var bvh = new DMeshAABBTree3(globalMesh, autoBuild: true);

        Log.LogInformation(
            "Built BVH on {Triangles} triangles ({Elements} elements)",
            globalMesh.TriangleCount, elementsList.Count);

        // Deterministic seed → identical classifications for identical inputs.
        var rng = new Random(42);

#if DEBUGMESH
        var debugSegments = new List<(Vector3d From, Vector3d To, bool Escape)>();
#endif

        var exteriorIds = new HashSet<string>(StringComparer.Ordinal);
        var totalRays   = 0L;

        foreach (var element in elementsList)
        {
            var anyExteriorTri = false;

            foreach (var tid in element.Mesh.TriangleIndices())
            {
                if (!element.Mesh.TryGetTriangleCentroidAndNormal(tid, out var centroid, out var n))
                {
                    continue;
                }

                var escapes = 0;
                for (var r = 0; r < _numRays; r++)
                {
                    var dir    = JitterDirection(n, _jitterRad, rng);
                    var origin = centroid + n * EPSILON;
                    var ray    = new Ray3d(origin, dir);
                    var hitTid = bvh.FindNearestHitTriangle(ray);

                    // Self-element hit ≡ ray clipped its own back face — treat as escape.
                    var escape = hitTid < 0 || ReferenceEquals(triToElement[hitTid], element);
                    if (escape)
                    {
                        escapes++;
                    }

#if DEBUGMESH
                    // Sample 1-in-100 to keep the viewer responsive on large models.
                    if (totalRays % 100 == 0)
                    {
                        debugSegments.Add((origin, origin + dir * 2.0, escape));
                    }
#endif
                    totalRays++;
                }

                if ((double)escapes / _numRays >= _hitRatio)
                {
                    anyExteriorTri = true;
                    break; // element-level answer is enough
                }
            }

            if (anyExteriorTri)
            {
                exteriorIds.Add(element.GlobalId);
            }
        }

#if DEBUGMESH
        var escapesDbg = debugSegments.Where(s => s.Escape).Select(s => (s.From, s.To)).ToList();
        var hitsDbg    = debugSegments.Where(s => !s.Escape).Select(s => (s.From, s.To)).ToList();
        if (escapesDbg.Count > 0)
        {
            GeometryDebug.Lines(escapesDbg, "#00aa00", "ray-escape");
        }
        if (hitsDbg.Count > 0)
        {
            GeometryDebug.Lines(hitsDbg, "#aa0000", "ray-hit");
        }
#endif

        var (classifications, exteriorFaces) = Classify(elementsList, exteriorIds);

        Log.LogInformation(
            "Ray casting done: {Ext} exterior / {Int} interior ({Rays} rays cast)",
            exteriorIds.Count, elementsList.Count - exteriorIds.Count, totalRays);

        return new DetectionResult(
            new Envelope(new DMesh3(), exteriorFaces),
            classifications.OrderBy(c => c.Element.GlobalId, StringComparer.Ordinal).ToList());
    }

    private static (DMesh3 mesh, BuildingElement[] triToElement) MergeMeshes(
        List<BuildingElement> elements)
    {
        var merged = new DMesh3();
        var owners = new List<BuildingElement>(elements.Sum(e => e.Mesh.TriangleCount));

        foreach (var element in elements)
        {
            var mesh = element.Mesh;
            // Vertex remap defends against non-dense vertex ids in source meshes.
            var vRemap = new Dictionary<int, int>(mesh.VertexCount);
            foreach (var vid in mesh.VertexIndices())
            {
                vRemap[vid] = merged.AppendVertex(mesh.GetVertex(vid));
            }

            foreach (var tid in mesh.TriangleIndices())
            {
                var t = mesh.GetTriangle(tid);
                merged.AppendTriangle(new Index3i(vRemap[t.a], vRemap[t.b], vRemap[t.c]));
                owners.Add(element);
            }
        }

        return (merged, owners.ToArray());
    }

    // Cone sample around `normal` with half-angle ≤ maxAngleRad.
    private static Vector3d JitterDirection(Vector3d normal, double maxAngleRad, Random rng)
    {
        if (maxAngleRad <= 0)
        {
            return normal;
        }

        var theta = rng.NextDouble() * maxAngleRad;
        var phi   = rng.NextDouble() * 2.0 * Math.PI;

        // Orthonormal basis around `normal`.
        var helper = Math.Abs(normal.x) > 0.9 ? new Vector3d(0, 1, 0) : new Vector3d(1, 0, 0);
        var u = normal.Cross(helper).Normalized;
        var v = normal.Cross(u);

        var sinT = Math.Sin(theta);
        var cosT = Math.Cos(theta);
        return (normal * cosT + u * (sinT * Math.Cos(phi)) + v * (sinT * Math.Sin(phi))).Normalized;
    }

    private (List<ElementClassification>, List<Face>) Classify(
        List<BuildingElement> elements,
        HashSet<string> exteriorIds)
    {
        var classifications = new List<ElementClassification>(elements.Count);
        var allFaces        = new List<Face>();

        foreach (var element in elements)
        {
            var isExterior = exteriorIds.Contains(element.GlobalId);
            var faces      = isExterior ? _faceExtractor.Extract(element) : Array.Empty<Face>();
            classifications.Add(new ElementClassification(element, isExterior, faces));
            if (isExterior)
            {
                allFaces.AddRange(faces);
            }
        }

        return (classifications, allFaces);
    }

    private static DetectionResult EmptyResult()
        => new(new Envelope(new DMesh3(), Array.Empty<Face>()),
               Array.Empty<ElementClassification>());
}
