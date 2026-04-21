using System.Diagnostics;
using g4;
using IfcEnvelopeMapper.Geometry.Primitives;
using IfcEnvelopeMapper.Geometry.Serialization;
using IfcEnvelopeMapper.Geometry.Voxel;

namespace IfcEnvelopeMapper.Geometry.Debug;

// Geometric debugger for algorithm development.
// Each method appends a shape and immediately flushes to C:\temp\ifc-debug-output.glb.
// Place an IDE breakpoint on the line after the call — the file is already written
// by the time execution pauses. Open http://localhost:5173/ in a browser (the
// viewer polls the GLB file every second) to inspect the current geometric state.
//
// Call-site semantics:
//   • [Conditional("DEBUG")] on every public method means any caller compiled
//     without the DEBUG symbol has the call stripped at compile time (no IL
//     instruction emitted). Release builds pay zero cost — no wrappers needed.
//   • The static constructor auto-starts the HTTP viewer iff a debugger is
//     attached. CI (`dotnet test`, no debugger) skips it: no port binding.
//     VS F5 or "Debug Test" attaches → viewer starts once per AppDomain.
public static class GeometryDebug
{
    // %TEMP% (AppData\Local\Temp) is blocked by Chromium's File System Access
    // API as a "system folder" so the file-picker-based fallback cannot read
    // it. C:\temp is also where the CLI runs from (Google Drive Streaming
    // native-DLL workaround), so everything lives in the same folder.
    private static readonly string OutputPath =
        Path.Combine(@"C:\temp", "ifc-debug-output.glb");

    private static readonly List<DebugShape> _shapes = new();

    // Static constructor runs once per AppDomain on first member touch.
    // Wrapped in try/catch: type-initializer exceptions are sticky — they
    // permanently brick the type for the process lifetime. A failed viewer
    // start shouldn't take the ability to log shapes down with it.
    static GeometryDebug()
    {
        if (!Debugger.IsAttached)
        {
            return;
        }

        try
        {
            var viewerHtml = FindUpward("tools/debug-viewer/index.html");
            if (viewerHtml is null)
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath)!);
            DebugViewerServer.Start(5173, viewerHtml, OutputPath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[GeometryDebug] viewer start failed: {ex.Message}");
        }
    }

    // ── High-level shape API (all [Conditional("DEBUG")]) ───────────────────

    [Conditional("DEBUG")]
    public static void Mesh(DMesh3 mesh, string color = "#ff0000", string label = "") =>
        Add(new MeshShape(mesh, color, label));

    // Batched: collapses N meshes into one MeshShape so the viewer shows one
    // layer per call (not N identical buttons when emitting a per-group layer).
    [Conditional("DEBUG")]
    public static void Meshes(IEnumerable<DMesh3> meshes, string color = "#cccccc", string label = "") =>
        Add(new MeshShape(Merge(meshes), color, label));

    // Slice of a mesh by triangle IDs — extracts just those tris into a new DMesh3.
    [Conditional("DEBUG")]
    public static void Triangles(DMesh3 mesh, IEnumerable<int> triangleIds,
                                  string color = "#ff0000", string label = "") =>
        Add(new MeshShape(ExtractTriangles(mesh, triangleIds), color, label));

    [Conditional("DEBUG")]
    public static void Voxels(VoxelGrid3D grid, IEnumerable<VoxelCoord> coords,
                               string color = "#0000ff", string label = "") =>
        Add(new MeshShape(GeometricPrimitives.VoxelCubes(grid, coords, shrinkFactor: 0.25), color, label));

    [Conditional("DEBUG")]
    public static void Points(IEnumerable<Vector3d> points, string color = "#ffff00", string label = "") =>
        Add(new PointsShape(points.ToArray(), color, label));

    [Conditional("DEBUG")]
    public static void Line(Vector3d from, Vector3d to, string color = "#ffffff", string label = "") =>
        Add(new LinesShape([(from, to)], color, label));

    [Conditional("DEBUG")]
    public static void Lines(IEnumerable<(Vector3d From, Vector3d To)> segments,
                              string color = "#ffffff", string label = "") =>
        Add(new LinesShape(segments.ToArray(), color, label));

    [Conditional("DEBUG")]
    public static void Box(AxisAlignedBox3d box, string color = "#00ffff", string label = "") =>
        Add(new LinesShape(GeometricPrimitives.BoxWireframe(box).ToArray(), color, label));

    [Conditional("DEBUG")]
    public static void Plane(Plane3d plane, double displaySize = 1.0,
                              string color = "#00ff00", string label = "") =>
        Add(new MeshShape(GeometricPrimitives.PlaneQuad(plane, displaySize), color, label));

    [Conditional("DEBUG")]
    public static void Sphere(Vector3d center, double radius,
                               string color = "#ff00ff", string label = "") =>
        Add(new MeshShape(GeometricPrimitives.Sphere(center, radius), color, label));

    [Conditional("DEBUG")]
    public static void Normal(Vector3d origin, Vector3d direction, double length = 0.5,
                               string color = "#ffff00", string label = "") =>
        Add(new LinesShape([(origin, origin + direction * length)], color, label));

    [Conditional("DEBUG")]
    public static void Clear()
    {
        _shapes.Clear();
        GltfSerializer.Flush(_shapes, OutputPath);
    }

    // ── Internals ────────────────────────────────────────────────────────────

    private static void Add(DebugShape shape)
    {
        _shapes.Add(shape);
        GltfSerializer.Flush(_shapes, OutputPath);
    }

    // Merges many DMesh3 into one — preserves winding; vertex indices get offset.
    private static DMesh3 Merge(IEnumerable<DMesh3> meshes)
    {
        var merged = new DMesh3();
        foreach (var m in meshes)
        {
            var offset = merged.VertexCount;
            for (var vid = 0; vid < m.MaxVertexID; vid++)
            {
                if (m.IsVertex(vid))
                {
                    merged.AppendVertex(m.GetVertex(vid));
                }
            }
            for (var tid = 0; tid < m.MaxTriangleID; tid++)
            {
                if (!m.IsTriangle(tid))
                {
                    continue;
                }
                var t = m.GetTriangle(tid);
                merged.AppendTriangle(new Index3i(t.a + offset, t.b + offset, t.c + offset));
            }
        }
        return merged;
    }

    private static DMesh3 ExtractTriangles(DMesh3 source, IEnumerable<int> triangleIds)
    {
        var mesh = new DMesh3();
        foreach (var tid in triangleIds)
        {
            if (!source.IsTriangle(tid))
            {
                continue;
            }
            var t  = source.GetTriangle(tid);
            var a  = mesh.AppendVertex(source.GetVertex(t.a));
            var b  = mesh.AppendVertex(source.GetVertex(t.b));
            var c  = mesh.AppendVertex(source.GetVertex(t.c));
            mesh.AppendTriangle(new Index3i(a, b, c));
        }
        return mesh;
    }

    // Walks up from CWD looking for a repo-relative path. Lets the same code
    // work from bin/Debug/net8.0/, from the CLI's C:\temp working dir, or
    // from a test host's output folder — whichever is active when the static
    // ctor fires. Single copy: CLI and test fixture previously had their own.
    private static string? FindUpward(string relative)
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        return null;
    }
}
