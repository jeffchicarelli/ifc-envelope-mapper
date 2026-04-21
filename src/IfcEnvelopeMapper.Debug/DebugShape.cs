using g4;

namespace IfcEnvelopeMapper.Debug;

// Three payloads matching the three glTF primitive topologies.
// All high-level shapes (Box, Sphere, Plane, Voxels, Normal) are pre-generated
// into one of these three by GeometryDebug before ever reaching the shape list.
internal abstract record DebugShape(string Color, string Label);

internal sealed record MeshShape(DMesh3 Mesh, string Color, string Label)
    : DebugShape(Color, Label);

internal sealed record LinesShape((Vector3d From, Vector3d To)[] Segments, string Color, string Label)
    : DebugShape(Color, Label);

internal sealed record PointsShape(Vector3d[] Points, string Color, string Label)
    : DebugShape(Color, Label);
