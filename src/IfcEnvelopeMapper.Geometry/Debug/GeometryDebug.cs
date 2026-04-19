#if DEBUG
using g4;

namespace IfcEnvelopeMapper.Geometry.Debug;

// Geometric debugger for algorithm development.
// Each method appends a shape and immediately flushes to %TEMP%\ifc-debug-output.gltf.
// Place an IDE breakpoint on the line after the call — the file is already written
// by the time execution pauses. Open tools/debug-viewer/index.html in a browser
// (it polls the file every second) to inspect the current geometric state.
public static class GeometryDebug
{
    private static readonly string OutputPath =
        Path.Combine(Path.GetTempPath(), "ifc-debug-output.gltf");

    private static readonly List<DebugShape> _shapes = new();

    public static void Mesh(DMesh3 mesh, string color = "#ff0000", string label = "") =>
        Add(new MeshShape(mesh, color, label));

    public static void Triangles(DMesh3 mesh, IEnumerable<int> triangleIds, string color = "#ff0000", string label = "") =>
        Add(new TrianglesShape(mesh, triangleIds.ToArray(), color, label));

    public static void Points(IEnumerable<Vector3d> points, string color = "#ffff00", float radius = 0.05f, string label = "") =>
        Add(new PointsShape(points.ToArray(), radius, color, label));

    public static void Line(Vector3d from, Vector3d to, string color = "#ffffff", float width = 0.01f, string label = "") =>
        Add(new LineShape(from, to, width, color, label));

    public static void Lines(IEnumerable<(Vector3d From, Vector3d To)> segments, string color = "#ffffff", float width = 0.01f, string label = "") =>
        Add(new LinesShape(segments.ToArray(), width, color, label));

    public static void Box(AxisAlignedBox3d box, string color = "#00ffff", string label = "") =>
        Add(new BoxShape(box, color, label));

    public static void Plane(Plane3d plane, double displaySize = 1.0, string color = "#00ff00", string label = "") =>
        Add(new PlaneShape(plane, displaySize, color, label));

    public static void Sphere(Vector3d center, double radius, string color = "#ff00ff", string label = "") =>
        Add(new SphereShape(center, radius, color, label));

    public static void Normal(Vector3d origin, Vector3d direction, double length = 0.5, string color = "#ffff00", string label = "") =>
        Add(new NormalShape(origin, direction, length, color, label));

    public static void Clear()
    {
        _shapes.Clear();
        Flush();
    }

    private static void Add(DebugShape shape)
    {
        _shapes.Add(shape);
        Flush();
    }

    private static void Flush()
    {
        // TODO Phase 2: serialise _shapes to glTF via SharpGLTF
        // File.WriteAllBytes(OutputPath, GltfSerializer.Serialise(_shapes));
    }
}

// Discriminated union — one concrete subtype per shape primitive.
internal abstract record DebugShape(string Color, string Label);
internal sealed record MeshShape(DMesh3 Mesh, string Color, string Label)                                   : DebugShape(Color, Label);
internal sealed record TrianglesShape(DMesh3 Mesh, int[] TriangleIds, string Color, string Label)           : DebugShape(Color, Label);
internal sealed record PointsShape(Vector3d[] Points, float Radius, string Color, string Label)             : DebugShape(Color, Label);
internal sealed record LineShape(Vector3d From, Vector3d To, float Width, string Color, string Label)       : DebugShape(Color, Label);
internal sealed record LinesShape((Vector3d, Vector3d)[] Segments, float Width, string Color, string Label) : DebugShape(Color, Label);
internal sealed record BoxShape(AxisAlignedBox3d Box, string Color, string Label)                           : DebugShape(Color, Label);
internal sealed record PlaneShape(Plane3d Plane, double DisplaySize, string Color, string Label)            : DebugShape(Color, Label);
internal sealed record SphereShape(Vector3d Center, double Radius, string Color, string Label)              : DebugShape(Color, Label);
internal sealed record NormalShape(Vector3d Origin, Vector3d Direction, double Length, string Color, string Label) : DebugShape(Color, Label);
#endif
