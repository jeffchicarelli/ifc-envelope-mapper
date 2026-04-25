using g4;
using IfcEnvelopeMapper.Core.Pipeline.Detection;
using IfcEnvelopeMapper.Core.Domain.Element;
using IfcEnvelopeMapper.Core.Domain.Surface;
using IfcEnvelopeMapper.Core.Extensions;

namespace IfcEnvelopeMapper.Engine.Strategies;

/// <summary>
/// Extracts planar <see cref="Face"/>s from a <see cref="BuildingElement"/> mesh
/// by grouping near-coplanar triangles, then fitting a best-fit plane per group
/// with PCA (Principal Component Analysis, <c>g4.OrthogonalPlaneFit3</c>).
///
///      raw mesh                    grouped by n̂                  Face per group
///    ┌───────────┐                ┌─────────────┐                ┌───────────────┐
///    │ △ △ △ △ △ │                │ ▲ ▲ ▲ │ ◆ ◆ │                │ plane + tri-  │
///    │ △ △ △ △ △ │ ── step 1+2 ──▶│ ▲ ▲ ▲ │ ◆ ◆ │ ── step 3+4 ──▶│ ids + area +  │
///    │ △ △ △ △ △ │  group by      │ ▲ ▲ ▲ │ ◆ ◆ │ split by       │ centroid      │
///    └───────────┘  normal        └─────────────┘ distance, fit   └───────────────┘
///
/// Fitting the plane from all vertex points (rather than reusing one triangle's
/// normal) is more robust on slightly curved or noisy surfaces — common in IFC
/// exports where a logically flat wall can carry small triangulation wobble.
/// </summary>
public sealed class PcaFaceExtractor : IFaceExtractor
{
    private readonly double _normalAngleTolerance;   // radians
    private readonly double _planeDistanceTolerance; // metres

    public PcaFaceExtractor(
        double normalAngleToleranceDeg = 5.0,
        double planeDistanceTolerance  = 0.05)
    {
        _normalAngleTolerance   = normalAngleToleranceDeg * Math.PI / 180.0;
        _planeDistanceTolerance = planeDistanceTolerance;
    }

    public IReadOnlyList<Face> Extract(BuildingElement element)
    {
        var mesh = element.Mesh;
        if (mesh.TriangleCount == 0)
        {
            return Array.Empty<Face>();
        }

        // Step 1+2: group triangles by similar normal direction
        var groups = new List<List<int>>();

        foreach (var tid in mesh.TriangleIndices())
        {
            if (!mesh.TryGetTriangleCentroidAndNormal(tid, out _, out var normal))
            {
                continue; // degenerate triangle
            }

            var placed = false;
            foreach (var group in groups)
            {
                if (!mesh.TryGetTriangleCentroidAndNormal(group[0], out _, out var repNormal))
                {
                    continue;
                }

                var dot   = Math.Clamp(normal.Dot(repNormal), -1.0, 1.0);
                var angle = Math.Acos(dot);

                // accept same-direction OR antiparallel normals (opposite faces of same wall)
                if (angle < _normalAngleTolerance || Math.PI - angle < _normalAngleTolerance)
                {
                    group.Add(tid);
                    placed = true;
                    break;
                }
            }

            if (!placed)
            {
                groups.Add([tid]);
            }
        }

        // Step 3+4: split each group by plane distance, then fit plane → Face
        var faces = new List<Face>();
        foreach (var group in groups)
        {
            faces.AddRange(SplitByDistanceAndFit(element, group));
        }

        return faces;
    }

    private IEnumerable<Face> SplitByDistanceAndFit(BuildingElement element, List<int> group)
    {
        var mesh = element.Mesh;
        if (!mesh.TryGetTriangleCentroidAndNormal(group[0], out _, out var repNormal))
        {
            yield break;
        }

        var subgroups = new List<List<int>>();

        // Step 3: subdivide by signed distance along the group's normal axis
        foreach (var tid in group)
        {
            if (!mesh.TryGetTriangleCentroidAndNormal(tid, out var centroid, out _))
            {
                continue;
            }

            var dist   = centroid.Dot(repNormal);
            var placed = false;

            foreach (var sub in subgroups)
            {
                if (!mesh.TryGetTriangleCentroidAndNormal(sub[0], out var subCentroid, out _))
                {
                    continue;
                }

                var repDist = subCentroid.Dot(repNormal);
                if (Math.Abs(dist - repDist) < _planeDistanceTolerance)
                {
                    sub.Add(tid);
                    placed = true;
                    break;
                }
            }

            if (!placed)
            {
                subgroups.Add([tid]);
            }
        }

        // Step 4: fit plane via PCA and create a Face per subgroup
        foreach (var sub in subgroups)
        {
            var points           = new List<Vector3d>();
            var area             = 0.0;
            var weightedCentroid = Vector3d.Zero;

            foreach (var tid in sub)
            {
                var t  = mesh.GetTriangle(tid);
                var va = mesh.GetVertex(t.a);
                var vb = mesh.GetVertex(t.b);
                var vc = mesh.GetVertex(t.c);
                points.Add(va);
                points.Add(vb);
                points.Add(vc);

                // area = 0.5 * |(vb - va) × (vc - va)|
                var triArea      = 0.5 * (vb - va).Cross(vc - va).Length;
                var triCentroid  = (va + vb + vc) / 3.0;
                area            += triArea;
                weightedCentroid += triCentroid * triArea;
            }

            var centroid = area > 1e-10 ? weightedCentroid / area : points[0];
            var plane    = points.FitPlane();

            yield return new Face(element, sub, plane, area, centroid);
        }
    }
}
