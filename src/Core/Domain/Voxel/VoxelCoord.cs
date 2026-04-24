namespace IfcEnvelopeMapper.Core.Domain.Voxel;

/// <summary>
/// Integer cell index into a <see cref="VoxelGrid3D"/>. The cell at <c>(0,0,0)</c>
/// is the one touching <see cref="VoxelGrid3D.Bounds"/>.Min; indices grow along +X, +Y, +Z.
/// </summary>
public readonly record struct VoxelCoord(int X, int Y, int Z);
