namespace IfcEnvelopeMapper.Core.Domain.Voxel;

/// <summary>
/// Labels a voxel as it moves through the 3-phase flood fill pipeline:
/// <c>Unknown → Occupied</c> (rasterization), then <c>Exterior / Interior</c>
/// (flood fill), then <c>Void</c> rooms (connected-component labelling).
/// </summary>
public enum VoxelState : byte
{
    /// <summary>Initial state — not yet processed.</summary>
    Unknown  = 0,

    /// <summary>A mesh triangle intersects this voxel.</summary>
    Occupied = 1,

    /// <summary>Outside the building — reached by flood fill from the grid corner.</summary>
    Exterior = 2,

    /// <summary>Inside the building — <c>Unknown</c> voxels that the exterior fill never reached.</summary>
    Interior = 3,

    /// <summary>A distinct interior room; numbered by <c>GrowVoid</c>.</summary>
    Void     = 4
}
