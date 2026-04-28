using g4;

namespace IfcEnvelopeMapper.Infrastructure.Visualization.Api;

/// <summary>
/// Abstract base for the three glTF payload kinds (Mesh, Lines, Points). All high-level shapes (Box, Sphere,
/// Plane, Voxels, Normal) are pre-generated into one of these three by <see cref="GeometryDebug"/> before
/// reaching <see cref="Scene"/>.
/// </summary>
internal abstract record DebugShape(Color Color, string Label);

/// <summary>
/// Triangle mesh payload. When <c>GlobalId</c> is non-null the serializer emits a dedicated glTF node and
/// writes <c>{ globalId, ifcType }</c> to <c>node.extras</c> for click-picking in the debug viewer.
/// </summary>
internal sealed record MeshShape(DMesh3 Mesh, Color Color, string Label, string? GlobalId = null) : DebugShape(Color, Label);

/// <summary>Line-segment payload. Each pair is rendered as a LINES glTF primitive.</summary>
internal sealed record LinesShape((Vector3d From, Vector3d To)[] Segments, Color Color, string Label) : DebugShape(Color, Label);

/// <summary>Point-cloud payload rendered as a POINTS glTF primitive.</summary>
internal sealed record PointsShape(Vector3d[] Points, Color Color, string Label) : DebugShape(Color, Label);
