#define DEBUGMESH
#if RELEASE
#undef DEBUGMESH
#endif

using g4;
using IfcEnvelopeMapper.Core.Pipeline.Detection;
using IfcEnvelopeMapper.Core.Domain.Element;
using IfcEnvelopeMapper.Core.Domain.Surface;
using IfcEnvelopeMapper.Core.Extensions;
using IfcEnvelopeMapper.Core.Domain.Voxel;

// Debug is a Debug-config-only ProjectReference — this using has to match
// the same gate as the call sites below, otherwise Release can't resolve
// the namespace.
#if DEBUGMESH
using IfcEnvelopeMapper.Engine.Visualization;
#endif

namespace IfcEnvelopeMapper.Engine.Strategies;

public sealed class VoxelFloodFillStrategy : IDetectionStrategy
{
    private readonly double _voxelSize;
    private readonly IFaceExtractor _faceExtractor;

    public VoxelFloodFillStrategy(double voxelSize = 0.5, IFaceExtractor? faceExtractor = null)
    {
        _voxelSize = voxelSize;
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
        // Per-element emission: each mesh becomes its own glTF node tagged with
        // { globalId, ifcType } in extras. The viewer groups nodes by ifcType
        // for layer buttons and uses globalId for click-picking/highlighting.
        foreach (var element in elementsList)
        {
            GeometryDebug.Element(element.Mesh, element.GlobalId, element.IfcType,
                IfcTypePalette.For(element.IfcType));
        }
#endif

        var grid = BuildGrid(elementsList);
        Rasterize(grid, elementsList);

#if DEBUGMESH
        // Sidecar JSON { "x,y,z": [globalIds...] } for viewer click-picking.
        // Emitted right after rasterize — occupants only grow during Rasterize,
        // so writing here captures the final mapping while the user can still
        // step through the flood-fill stages.
        SidecarWriter.WriteVoxelOccupants(grid);

        var occupied = grid.VoxelsByState(VoxelState.Occupied).ToList();
        Console.WriteLine($"occupied voxels: {occupied.Count}");
        GeometryDebug.Voxels(grid, occupied, "#00aa00c0", "occupied");
#endif

        grid.GrowExterior();
        grid.FillGaps();

#if DEBUGMESH
        var exterior = grid.VoxelsByState(VoxelState.Exterior).ToList();
        Console.WriteLine($"exterior voxels: {exterior.Count}");
        GeometryDebug.Voxels(grid, exterior, "#0055ffc0", "exterior");
#endif

        grid.GrowInterior();

#if DEBUGMESH
        var interior = grid.VoxelsByState(VoxelState.Interior).ToList();
        Console.WriteLine($"interior voxels: {interior.Count}");
        GeometryDebug.Voxels(grid, interior, "#ff0000c0", "interior");
#endif

        grid.GrowVoid();

        var exteriorIds = FindExteriorIds(grid);
        var (classifications, exteriorFaces) = Classify(elementsList, exteriorIds);

        return new DetectionResult(
            new Envelope(new DMesh3(), exteriorFaces),
            classifications.OrderBy(c => c.Element.GlobalId, StringComparer.Ordinal).ToList());
    }

    private VoxelGrid3D BuildGrid(List<BuildingElement> elements)
    {
        var bbox = elements.BoundingBox();

        // Pad by 2*voxelSize so the corner voxel (0,0,0) is guaranteed outside the model.
        var pad = 2.0 * _voxelSize;
        var expanded = new AxisAlignedBox3d(
            bbox.Min - new Vector3d(pad, pad, pad),
            bbox.Max + new Vector3d(pad, pad, pad));

        return new VoxelGrid3D(expanded, _voxelSize);
    }

    private void Rasterize(VoxelGrid3D grid, List<BuildingElement> elements)
    {
        foreach (var element in elements)
        {
            var mesh = element.Mesh;
            for (var tid = 0; tid < mesh.MaxTriangleID; tid++)
            {
                if (!mesh.IsTriangle(tid))
                {
                    continue;
                }

                var tri = mesh.GetTriangle(tid);
                var v0  = mesh.GetVertex(tri.a);
                var v1  = mesh.GetVertex(tri.b);
                var v2  = mesh.GetVertex(tri.c);

                var triBbox = new AxisAlignedBox3d(v0, v0);
                triBbox.Contain(v1);
                triBbox.Contain(v2);

                foreach (var coord in grid.VoxelsInBbox(triBbox))
                {
                    var center = grid.VoxelToCenter(coord);
                    var hs     = _voxelSize * 0.5;
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
        List<BuildingElement> elements,
        HashSet<string> exteriorIds)
    {
        var classifications = new List<ElementClassification>(elements.Count);
        var allFaces        = new List<Face>();

        foreach (var element in elements)
        {
            var isExterior   = exteriorIds.Contains(element.GlobalId);
            var faces        = isExterior
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
        var h   = new Vector3d(box.Width * 0.5, box.Height * 0.5, box.Depth * 0.5);
        var a   = v0 - ctr;
        var b   = v1 - ctr;
        var d   = v2 - ctr;

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

        var e0     = b - a;
        var e1     = d - b;
        var e2     = a - d;
        var normal = e0.Cross(e1);
        if (normal.LengthSquared > 1e-20 && !OverlapsOnAxis(a, b, d, h, normal))
        {
            return false;
        }

        Vector3d[] edges    = [e0, e1, e2];
        Vector3d[] boxAxes  = [new(1, 0, 0), new(0, 1, 0), new(0, 0, 1)];

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
        var r  = h.x * Math.Abs(axis.x) + h.y * Math.Abs(axis.y) + h.z * Math.Abs(axis.z);
        return !(Math.Min(Math.Min(pa, pb), pc) > r || Math.Max(Math.Max(pa, pb), pc) < -r);
    }

    private static DetectionResult EmptyResult()
    {
        return new DetectionResult(new Envelope(new DMesh3(), Array.Empty<Face>()),
            Array.Empty<ElementClassification>());
    }
}
