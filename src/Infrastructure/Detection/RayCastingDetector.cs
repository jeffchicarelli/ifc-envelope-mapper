#undef DEBUGMESH
#if RELEASE
#undef DEBUGMESH
#endif

using g4;
using IfcEnvelopeMapper.Domain.Detection;
using IfcEnvelopeMapper.Domain.Extensions;
using IfcEnvelopeMapper.Domain.Interfaces;
using IfcEnvelopeMapper.Domain.Services;
using IfcEnvelopeMapper.Domain.Surface;
using Microsoft.Extensions.Logging;

#if DEBUGMESH
using IfcEnvelopeMapper.Infrastructure.Visualization.Api;
#endif

using static IfcEnvelopeMapper.Infrastructure.Diagnostics.AppLog;

namespace IfcEnvelopeMapper.Infrastructure.Detection;

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
public sealed class RayCastingDetector : IEnvelopeDetector
{
    private const double EPSILON  = 1e-6;
    private const int    RngSeed  = 42;

    private readonly int _numRays;
    private readonly double _jitterRad;
    private readonly double _hitRatio;
    private readonly IFaceExtractor _faceExtractor;

    /// <summary>Creates a detector with the given per-triangle ray parameters.</summary>
    public RayCastingDetector(
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

    /// <summary>Returns the active ray-casting parameters as a serialisable config snapshot.</summary>
    public StrategyConfig Config =>
        new(VoxelSize: null, NumRays: _numRays,
            JitterDeg: _jitterRad * 180.0 / Math.PI, HitRatio: _hitRatio);

    /// <inheritdoc/>
    public DetectionResult Detect(IEnumerable<IElement> elements)
    {
        var elementsList = elements.ToList();
        if (elementsList.Count == 0)
        {
            return EmptyResult();
        }

#if DEBUGMESH
        GeometryDebug.Send(elementsList);
#endif

        var (globalMesh, triToElement) = MergeMeshes(elementsList);
        var bvh = new DMeshAABBTree3(globalMesh, autoBuild: true);

        Log.LogInformation(
            "Built BVH on {Triangles} triangles ({Elements} elements)",
            globalMesh.TriangleCount, elementsList.Count);

        // Deterministic seed → identical classifications for identical inputs.
        var rng = new Random(RngSeed);

#if DEBUGMESH
        var debugSegments = new List<(Vector3d From, Vector3d To, bool Escape)>();
#endif

        var exteriorIds = new HashSet<string>(StringComparer.Ordinal);
        var totalRays   = 0L;

        foreach (var element in elementsList)
        {
            var anyExteriorTri = false;

            foreach (var tid in element.GetMesh().TriangleIndices())
            {
                if (!element.GetMesh().TryGetTriangleCentroidAndNormal(tid, out var centroid, out var n))
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
                    break;
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
            GeometryDebug.Send(escapesDbg, Color.FromHex("#00aa00"), "ray-escape");
        }
        if (hitsDbg.Count > 0)
        {
            GeometryDebug.Send(hitsDbg, Color.FromHex("#aa0000"), "ray-hit");
        }
#endif

        var (classifications, exteriorFaces) = Classify(elementsList, exteriorIds);

#if DEBUGMESH
        var externalElements = classifications
                              .Where(c => c.IsExterior)
                              .Select(c => c.Element)
                              .ToList();

        GeometryDebug.Send(externalElements, Color.Magenta);

        Log.LogInformation("exterior elements: {Count}", externalElements.Count);
#endif

        Log.LogInformation(
            "Ray casting done: {Ext} exterior / {Int} interior ({Rays} rays cast)",
            exteriorIds.Count, elementsList.Count - exteriorIds.Count, totalRays);

        return new DetectionResult(
            new Envelope(new DMesh3(), exteriorFaces),
            classifications.OrderBy(c => c.Element.GlobalId, StringComparer.Ordinal).ToList());
    }

    private static (DMesh3 mesh, IElement[] triToElement) MergeMeshes(List<IElement> elements)
    {
        var merged = new DMesh3();
        var owners = new List<IElement>(elements.Sum(e => e.GetMesh().TriangleCount));

        foreach (var element in elements)
        {
            var mesh = element.GetMesh();
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

        var helper = Math.Abs(normal.x) > 0.9 ? new Vector3d(0, 1, 0) : new Vector3d(1, 0, 0);
        var u = normal.Cross(helper).Normalized;
        var v = normal.Cross(u);

        var sinT = Math.Sin(theta);
        var cosT = Math.Cos(theta);
        return (normal * cosT + u * (sinT * Math.Cos(phi)) + v * (sinT * Math.Sin(phi))).Normalized;
    }

    private (List<ElementClassification>, List<Face>) Classify(
        List<IElement> elements,
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
