#undef DEBUGMESH
#if RELEASE
#undef DEBUGMESH
#endif

using g4;
using IfcEnvelopeMapper.Ifc.Domain;
using IfcEnvelopeMapper.Ifc.Domain.Surface;
using IfcEnvelopeMapper.Core.Extensions;
using IfcEnvelopeMapper.Core.Domain.Voxel;

// Debug is a Debug-config-only ProjectReference — this using has to match
// the same gate as the call sites below, otherwise Release can't resolve
// the namespace.
#if DEBUGMESH
using IfcEnvelopeMapper.Engine.Visualization.Api;
using Microsoft.Extensions.Logging;
using static IfcEnvelopeMapper.Core.Diagnostics.AppLog;
#endif

namespace IfcEnvelopeMapper.Engine.Pipeline.Detection;

/// <summary>
/// Detects exterior elements by rasterizing the model into a <see cref="VoxelGrid3D"/>
/// and flood-filling from outside in (van der Vaart 2022).
///
///   1) Rasterize: each mesh triangle marks the voxels it intersects as Occupied
///      via the SAT test (Akenine-Möller 1997). Occupants are tracked per voxel
///      so we can map voxels back to element GlobalIds.
///   2) GrowExterior: 26-connected flood fill from corner voxel (0,0,0), which
///      the padded grid guarantees to be outside the model.
///   3) FillGaps: close 1-voxel holes in the occupied shell caused by imperfect
///      IFC meshes, then GrowInterior and GrowVoid label the remaining cells.
///   4) Classify: any element occupying a voxel that touches an Exterior voxel
///      is itself exterior.
///
///        ┌─────────────────────┐                ┌─────────────────────┐
///        │ · · · · · · · · · · │                │ ◦ ◦ ◦ ◦ ◦ ◦ ◦ ◦ ◦ ◦ │     · Unknown
///        │ · ███████████ · · · │                │ ◦ ███████████ ◦ ◦ ◦ │     █ Occupied
///        │ · █         █ · · · │   flood fill   │ ◦ █ · · · · █ ◦ ◦ ◦ │     ◦ Exterior
///        │ · █         █ · · · │  ─────────────▶│ ◦ █ · · · · █ ◦ ◦ ◦ │
///        │ · █         █ · · · │                │ ◦ █ · · · · █ ◦ ◦ ◦ │
///        │ · ███████████ · · · │                │ ◦ ███████████ ◦ ◦ ◦ │
///        │ · · · · · · · · · · │                │ ◦ ◦ ◦ ◦ ◦ ◦ ◦ ◦ ◦ ◦ │
///        └─────────────────────┘                └─────────────────────┘
///
/// </summary>
/// <remarks>
/// <c>voxelSize</c> trades accuracy for cost: halving it multiplies rasterization
/// and flood-fill work by ~8. 0.5 m matches the default in the reference paper and
/// empirically resolves typical wall/slab thicknesses.
/// </remarks>
public sealed class VoxelFloodFillStrategy : IEnvelopeDetector
{
    private readonly double _voxelSize;
    private readonly IFaceExtractor _faceExtractor;

    public VoxelFloodFillStrategy(double voxelSize = 0.5, IFaceExtractor? faceExtractor = null)
    {
        _voxelSize = voxelSize;
        _faceExtractor = faceExtractor ?? new PcaFaceExtractor();
    }

    public DetectionResult Detect(IEnumerable<Element> elements)
    {
        var elementsList = elements.ToList();
        if (elementsList.Count == 0)
        {
            return EmptyResult();
        }

#if DEBUGMESH
        // Per-element emission: each mesh becomes its own glTF node tagged with
        // { globalId, ifcType } in extras. The viewer groups nodes by ifcType
        // for layer buttons and uses globalId for click-picking/highlighting.
        GeometryDebug.Send(elementsList);
#endif

        var grid = BuildGrid(elementsList);
        Rasterize(grid, elementsList);

#if DEBUGMESH
        Log.LogInformation("occupied voxels: {Count}", grid.VoxelsByState(VoxelState.Occupied).Count());
        GeometryDebug.Send(grid, VoxelState.Occupied);
#endif

        grid.GrowExterior();
        grid.FillGaps();

#if DEBUGMESH
        Log.LogInformation("exterior voxels: {Count}", grid.VoxelsByState(VoxelState.Exterior).Count());
        GeometryDebug.Send(grid, VoxelState.Exterior);
#endif

        grid.GrowInterior();

#if DEBUGMESH
        Log.LogInformation("interior voxels: {Count}", grid.VoxelsByState(VoxelState.Interior).Count());
        GeometryDebug.Send(grid, VoxelState.Interior);
#endif

        grid.GrowVoid();

#if DEBUGMESH
        Log.LogInformation("void voxels: {Count}", grid.VoxelsByState(VoxelState.Void).Count());
        GeometryDebug.Send(grid, VoxelState.Void);
#endif

        var exteriorIds = FindExteriorIds(grid);
        var (classifications, exteriorFaces) = Classify(elementsList, exteriorIds);

#if DEBUGMESH
        var externalElements = classifications
                              .Where(c => c.IsExterior)
                              .Select(c => c.Element)
                              .ToList();

        GeometryDebug.Send(externalElements, Color.Magenta);

        Log.LogInformation("exterior elements: {Count}", externalElements.Count);
#endif

        return new DetectionResult(
            new Envelope(new DMesh3(), exteriorFaces),
            classifications.OrderBy(c => c.Element.GlobalId, StringComparer.Ordinal).ToList());
    }

    private VoxelGrid3D BuildGrid(List<Element> elements)
    {
        var bbox = elements.BoundingBox();

        // Pad by 2*voxelSize so the corner voxel (0,0,0) is guaranteed outside the model.
        var pad = 2.0 * _voxelSize;
        var expanded = new AxisAlignedBox3d(
            bbox.Min - new Vector3d(pad, pad, pad),
            bbox.Max + new Vector3d(pad, pad, pad));

        return new VoxelGrid3D(expanded, _voxelSize);
    }

    private void Rasterize(VoxelGrid3D grid, List<Element> elements)
    {
        foreach (var element in elements)
        {
            var mesh = element.GetMesh();
            for (var tid = 0; tid < mesh.MaxTriangleID; tid++)
            {
                if (!mesh.IsTriangle(tid))
                {
                    continue;
                }

                var tri = mesh.GetTriangle(tid);
                var v0 = mesh.GetVertex(tri.a);
                var v1 = mesh.GetVertex(tri.b);
                var v2 = mesh.GetVertex(tri.c);

                var triBbox = new AxisAlignedBox3d(v0, v0);
                triBbox.Contain(v1);
                triBbox.Contain(v2);

                foreach (var coord in grid.VoxelsInBbox(triBbox))
                {
                    var center = grid.VoxelToCenter(coord);
                    var hs = _voxelSize * 0.5;
                    var voxelBox = new AxisAlignedBox3d(
                        center - new Vector3d(hs, hs, hs),
                        center + new Vector3d(hs, hs, hs));

                    if (!TriangleIntersectsAabb(v0, v1, v2, voxelBox))
                    {
                        continue;
                    }

                    if (grid[coord] == VoxelState.Unknown)
                    {
                        grid[coord] = VoxelState.Occupied;

                        // GeometryDebug.Voxels(grid, [coord], "#00aa0020", "occupied");
                    }

                    grid.AddOccupant(coord, element.GlobalId);
                }
            }
        }
    }

    private static HashSet<string> FindExteriorIds(VoxelGrid3D grid)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);

        for (var x = 0; x < grid.NX; x++)
        {
            for (var y = 0; y < grid.NY; y++)
            {
                for (var z = 0; z < grid.NZ; z++)
                {
                    var coord = new VoxelCoord(x, y, z);
                    if (grid[coord] != VoxelState.Occupied)
                    {
                        continue;
                    }

                    // A shell voxel is exterior if at least one 26-neighbor is Exterior.
                    // 26-connectivity matches GrowExterior, so no shell voxel is missed.
                    if (grid.Neighbors26(coord).All(n => grid[n] != VoxelState.Exterior))
                    {
                        continue;
                    }

                    foreach (var id in grid.OccupantsOf(coord))
                    {
                        ids.Add(id);
                    }
                }
            }
        }

        return ids;
    }

    private (List<ElementClassification>, List<Face>) Classify(
        List<Element> elements,
        HashSet<string> exteriorIds)
    {
        var classifications = new List<ElementClassification>(elements.Count);
        var allFaces = new List<Face>();

        foreach (var element in elements)
        {
            var isExterior = exteriorIds.Contains(element.GlobalId);
            var faces = isExterior
                ? _faceExtractor.Extract(element)
                : Array.Empty<Face>();

            classifications.Add(new ElementClassification(element, isExterior, faces));

            if (isExterior)
            {
                allFaces.AddRange(faces);
            }
        }

        return (classifications, allFaces);
    }

    // SAT (Separating Axis Theorem) — Akenine-Möller 1997.
    // 13 axes: 3 AABB face normals + 1 triangle normal + 9 edge cross-products.
    private static bool TriangleIntersectsAabb(Vector3d v0, Vector3d v1, Vector3d v2, AxisAlignedBox3d box)
    {
        // Translate to box-center frame so the AABB becomes [-h, +h] on each axis.
        var ctr = box.Center;
        var h = new Vector3d(box.Width * 0.5, box.Height * 0.5, box.Depth * 0.5);
        var a = v0 - ctr;
        var b = v1 - ctr;
        var d = v2 - ctr;

        if (!OverlapsOnAxis(a, b, d, h, new Vector3d(1, 0, 0)))
        {
            return false;
        }

        if (!OverlapsOnAxis(a, b, d, h, new Vector3d(0, 1, 0)))
        {
            return false;
        }

        if (!OverlapsOnAxis(a, b, d, h, new Vector3d(0, 0, 1)))
        {
            return false;
        }

        var e0 = b - a;
        var e1 = d - b;
        var e2 = a - d;
        var normal = e0.Cross(e1);
        if (normal.LengthSquared > 1e-20 && !OverlapsOnAxis(a, b, d, h, normal))
        {
            return false;
        }

        Vector3d[] edges = [e0, e1, e2];
        Vector3d[] boxAxes = [new(1, 0, 0), new(0, 1, 0), new(0, 0, 1)];

        foreach (var edge in edges)
        {
            foreach (var axis in boxAxes)
            {
                if (!OverlapsOnAxis(a, b, d, h, edge.Cross(axis)))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool OverlapsOnAxis(Vector3d a, Vector3d b, Vector3d c, Vector3d h, Vector3d axis)
    {
        if (axis.LengthSquared < 1e-10)
        {
            return true;
        }

        var pa = a.Dot(axis);
        var pb = b.Dot(axis);
        var pc = c.Dot(axis);
        var r = h.x * Math.Abs(axis.x) + h.y * Math.Abs(axis.y) + h.z * Math.Abs(axis.z);
        return !(Math.Min(Math.Min(pa, pb), pc) > r || Math.Max(Math.Max(pa, pb), pc) < -r);
    }

    private static DetectionResult EmptyResult()
    {
        return new DetectionResult(new Envelope(new DMesh3(), Array.Empty<Face>()),
            Array.Empty<ElementClassification>());
    }
}
