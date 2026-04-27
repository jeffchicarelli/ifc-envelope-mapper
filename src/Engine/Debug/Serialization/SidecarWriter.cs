using System.Text.Json;

using IfcEnvelopeMapper.Core.Domain.Voxel;

using IfcEnvelopeMapper.Engine.Debug.Api;

namespace IfcEnvelopeMapper.Engine.Debug.Serialization;

// Sidecar JSON for the debug viewer's voxel-pick feature. Lives next to the
// voxel strategy that produces it (only caller). Atomic writes match the GLB
// flush pattern: tmp + AtomicFile.MoveWithRetry so the viewer's pollers never
// trip on a mid-flight swap.
//
// Honours <see cref="GeometryDebug.Enabled"/> (CLI sets it false → no-op) and
// derives its output path from <c>DebugSession.OutputPath</c> so per-flow
// AsyncLocal isolation gives each xunit test method its own sidecar file —
// no cross-test races on the shared default `C:\temp` location.
public static class SidecarWriter
{
    // Emits { voxelSize, origin, nx, ny, nz, occupants: { "x,y,z": [...ids] } }.
    // Sparse: only cells with ≥1 occupant are written. Lets the viewer map a
    // clicked voxel back to the Elements that rasterized into it.
    public static void WriteVoxelOccupants(VoxelGrid3D grid)
    {
        if (!GeometryDebug.Enabled)
        {
            return;
        }

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

        // Derive sidecar path next to the GLB so per-flow AsyncLocal output
        // paths give each test its own sidecar file. e.g.
        //   GLB    : C:\temp\test-run-XYZ.glb
        //   sidecar: C:\temp\test-run-XYZ-occupants.json
        var glbPath     = DebugSession.OutputPath;
        var dir         = Path.GetDirectoryName(glbPath) ?? @"C:\temp";
        var stem        = Path.GetFileNameWithoutExtension(glbPath);
        var sidecarPath = Path.Combine(dir, stem + "-occupants.json");

        var tmp = sidecarPath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(payload));
        AtomicFile.MoveWithRetry(tmp, sidecarPath);
    }
}
