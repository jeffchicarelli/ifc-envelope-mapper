using System.Text.Json;

using IfcEnvelopeMapper.Core.Domain.Voxel;

using IfcEnvelopeMapper.Engine.Visualization.Api;

namespace IfcEnvelopeMapper.Engine.Visualization.Serialization;

// Voxel→element occupancy map for the debug viewer's click-pick feature.
// Emitted as a JSON file next to the GLB, keyed by voxel coordinate
// (sparse — only cells with ≥1 occupant). Lets the viewer translate a
// clicked voxel cube into the IFC elements that rasterized into it.
//
// Honours <see cref="GeometryDebug.Enabled"/> (CLI sets it false → no-op)
// and derives its output path from <c>Scene.OutputPath</c> so per-flow
// AsyncLocal isolation gives each xunit test method its own file — no
// cross-test races on the shared default `C:\temp` location.
//
// Atomic writes match the GLB flush pattern: tmp + AtomicFile.MoveWithRetry
// so the viewer's pollers never trip on a mid-flight swap.
internal static class VoxelOccupants
{
    // Emits { voxelSize, origin, nx, ny, nz, occupants: { "x,y,z": [...ids] } }.
    public static void Write(VoxelGrid3D grid)
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

        // Output path: same directory + stem as the current debug GLB,
        // with `-occupants.json` appended.
        //   GLB    : C:\temp\test-run-XYZ.glb
        //   output : C:\temp\test-run-XYZ-occupants.json
        var glbPath  = Scene.OutputPath;
        var dir      = Path.GetDirectoryName(glbPath) ?? @"C:\temp";
        var stem     = Path.GetFileNameWithoutExtension(glbPath);
        var jsonPath = Path.Combine(dir, stem + "-occupants.json");

        var tmp = jsonPath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(payload));
        AtomicFile.MoveWithRetry(tmp, jsonPath);
    }
}
