using g4;
using IfcEnvelopeMapper.Geometry.Voxel;

namespace IfcEnvelopeMapper.Geometry.Debug;

internal abstract record DebugShape(string Color, string Label);
internal sealed record MeshShape(DMesh3 Mesh, string Color, string Label)                                         : DebugShape(Color, Label);
internal sealed record MeshesShape(DMesh3[] Meshes, string Color, string Label)                                    : DebugShape(Color, Label);
internal sealed record TrianglesShape(DMesh3 Mesh, int[] TriangleIds, string Color, string Label)                 : DebugShape(Color, Label);
internal sealed record VoxelsShape(VoxelGrid3D Grid, VoxelCoord[] Coords, string Color, string Label)             : DebugShape(Color, Label);
internal sealed record PointsShape(Vector3d[] Points, float Radius, string Color, string Label)                   : DebugShape(Color, Label);
internal sealed record LineShape(Vector3d From, Vector3d To, float Width, string Color, string Label)             : DebugShape(Color, Label);
internal sealed record LinesShape((Vector3d, Vector3d)[] Segments, float Width, string Color, string Label)       : DebugShape(Color, Label);
internal sealed record BoxShape(AxisAlignedBox3d Box, string Color, string Label)                                 : DebugShape(Color, Label);
internal sealed record PlaneShape(Plane3d Plane, double DisplaySize, string Color, string Label)                  : DebugShape(Color, Label);
internal sealed record SphereShape(Vector3d Center, double Radius, string Color, string Label)                    : DebugShape(Color, Label);
internal sealed record NormalShape(Vector3d Origin, Vector3d Direction, double Length, string Color, string Label): DebugShape(Color, Label);
