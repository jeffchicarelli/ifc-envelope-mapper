using System.Text.Json;
using IfcEnvelopeMapper.Geometry.Voxel;

namespace IfcEnvelopeMapper.Debug;

// Sidecar JSON files served by DebugServer alongside the GLB. Kept separate
// from GltfSerializer so the facade (GeometryDebug) can delegate non-glTF
// viewer data without growing the glTF emitter's surface. Atomic writes
// match the GLB flush pattern: tmp + MoveWithRetry so the helper's FileShare
// readers never trip on a mid-flight swap.
internal static class SidecarWriter
{
    // Co-located with the GLB in C:\temp so the whole debug payload lives in
    // one folder the viewer's File System Access fallback can pick.
    private static readonly string OccupantsPath =
        Path.Combine(@"C:\temp", "ifc-debug-occupants.json");

    // Emits { voxelSize, origin, nx, ny, nz, occupants: { "x,y,z": [...ids] } }.
    // Sparse: only cells with ≥1 occupant are written. Lets the viewer map a
    // clicked voxel back to the BuildingElements that rasterized into it.
    public static void WriteVoxelOccupants(VoxelGrid3D grid)
    {
        var occupants = new Dictionary<string, string[]>();
        for (var x = 0; x < grid.NX; x++)
        {
            for (var y = 0; y < grid.NY; y++)
            {
                for (var z = 0; z < grid.NZ; z++)
                {
                    var coord = new VoxelCoord(x, y, z);
                    var ids   = grid.OccupantsOf(coord);
                    if (ids.Count == 0)
                    {
                        continue;
                    }
                    occupants[$"{x},{y},{z}"] = ids.ToArray();
                }
            }
        }

        var payload = new
        {
            voxelSize = grid.VoxelSize,
            origin    = new[] { grid.Bounds.Min.x, grid.Bounds.Min.y, grid.Bounds.Min.z },
            nx        = grid.NX,
            ny        = grid.NY,
            nz        = grid.NZ,
            occupants,
        };

        var tmp = OccupantsPath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(payload));
        GltfSerializer.MoveWithRetry(tmp, OccupantsPath);
    }
}
