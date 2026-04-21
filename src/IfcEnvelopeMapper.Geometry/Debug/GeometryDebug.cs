using g4;
using IfcEnvelopeMapper.Geometry.Voxel;

namespace IfcEnvelopeMapper.Geometry.Debug;

// Geometric debugger for algorithm development.
// Each method appends a shape and immediately flushes to %TEMP%\ifc-debug-output.gltf.
// Place an IDE breakpoint on the line after the call — the file is already written
// by the time execution pauses. Open tools/debug-viewer/index.html in a browser
// (it polls the file every second) to inspect the current geometric state.
public static class GeometryDebug
{
    // %TEMP% (AppData\Local\Temp) is blocked by Chromium's File System Access API
    // as a "system folder", so the debug-viewer cannot open it. C:\temp is also
    // where the CLI runs from (Google Drive Streaming native-DLL workaround).
    private static readonly string OutputPath =
        Path.Combine(@"C:\temp", "ifc-debug-output.glb");

    private static readonly List<DebugShape> _shapes = new();

    public static void Mesh(DMesh3 mesh, string color = "#ff0000", string label = "") =>
        Add(new MeshShape(mesh, color, label));

    public static void Meshes(IEnumerable<DMesh3> meshes, string color = "#cccccc", string label = "") =>
        Add(new MeshesShape(meshes.ToArray(), color, label));

    public static void Triangles(DMesh3 mesh, IEnumerable<int> triangleIds, string color = "#ff0000", string label = "") =>
        Add(new TrianglesShape(mesh, triangleIds.ToArray(), color, label));

    public static void Voxels(VoxelGrid3D grid, IEnumerable<VoxelCoord> coords, string color = "#0000ff", string label = "") =>
        Add(new VoxelsShape(grid, coords.ToArray(), color, label));

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
        GltfSerializer.Flush(_shapes, OutputPath);
    }

    private static void Add(DebugShape shape)
    {
        _shapes.Add(shape);
        GltfSerializer.Flush(_shapes, OutputPath);
    }
}
