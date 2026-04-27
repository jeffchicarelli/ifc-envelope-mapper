using g4;

namespace IfcEnvelopeMapper.Engine.Debug;

// Three payloads matching the three glTF primitive topologies.
// All high-level shapes (Box, Sphere, Plane, Voxels, Normal) are pre-generated
// into one of these three by GeometryDebug before ever reaching the shape list.
internal abstract record DebugShape(string Color, string Label);

// GlobalId is set only for per-element emissions from GeometryDebug.Element.
// When non-null, the serializer emits this mesh as its own glTF node (no merge
// with same-label siblings) and writes { globalId, ifcType } to node.extras so
// the viewer can identify each element for click-picking and highlighting.
internal sealed record MeshShape(DMesh3 Mesh, string Color, string Label, string? GlobalId = null)
    : DebugShape(Color, Label);

internal sealed record LinesShape((Vector3d From, Vector3d To)[] Segments, string Color, string Label)
    : DebugShape(Color, Label);

internal sealed record PointsShape(Vector3d[] Points, string Color, string Label)
    : DebugShape(Color, Label);
