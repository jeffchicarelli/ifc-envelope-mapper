namespace IfcEnvelopeMapper.Geometry.Voxel;

public enum VoxelState : byte
{
    Unknown  = 0,   // initial state — not yet processed
    Occupied = 1,   // a mesh triangle intersects this voxel
    Exterior = 2,   // outside the building — reached by flood fill
    Interior = 3,   // inside the building — unreachable from exterior
    Void     = 4    // distinct interior room, numbered by GrowVoid
}
