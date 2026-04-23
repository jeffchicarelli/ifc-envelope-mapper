using System.Diagnostics;
using System.Text.Json;
using g4;
using IfcEnvelopeMapper.Geometry.Primitives;
using IfcEnvelopeMapper.Geometry.Voxel;

namespace IfcEnvelopeMapper.Debug;

// Geometric debugger for algorithm development.
// Each method appends a shape and immediately flushes to C:\temp\ifc-debug-output.glb.
// Place an IDE breakpoint on the line after the call — the file is already written
// by the time execution pauses. Open http://localhost:5173/ in a browser (the
// viewer polls the GLB file 5×/second) to inspect the current geometric state.
//
// Call-site semantics:
//   • [Conditional("DEBUG")] on every public method means any caller compiled
//     without the DEBUG symbol has the call stripped at compile time (no IL
//     instruction emitted). Release builds pay zero cost — no wrappers needed.
//   • The static constructor spawns the viewer HTTP server as a SEPARATE OS
//     process (IfcEnvelopeMapper.DebugServer.exe). This isolates the server
//     from the .NET debugger attached to this process: breakpoints freeze
//     managed threads in-process but have no authority over the helper's
//     scheduler, so the browser keeps getting responses while the CLI is
//     paused.
public static class GeometryDebug
{
    // %TEMP% (AppData\Local\Temp) is blocked by Chromium's File System Access
    // API as a "system folder" so the file-picker-based fallback cannot read
    // it. C:\temp is also where the CLI runs from (Google Drive Streaming
    // native-DLL workaround), so everything lives in the same folder.
    private static readonly string OutputPath =
        Path.Combine(@"C:\temp", "ifc-debug-output.glb");

    // Sidecar JSON served by DebugServer alongside the GLB. Populated by
    // GeometryDebug.VoxelOccupants to let the viewer map a clicked voxel
    // back to the building elements that rasterized into it.
    private static readonly string OccupantsPath =
        Path.Combine(@"C:\temp", "ifc-debug-occupants.json");

    private static readonly List<DebugShape> _shapes = new();

    // Handle to the out-of-process HTTP viewer server. Kept at type scope so the
    // ProcessExit handler can terminate it when the CLI shuts down cleanly.
    private static Process? _helperProcess;

    // Static constructor runs once per AppDomain on first member touch.
    //
    // No Debugger.IsAttached gate: [Conditional("DEBUG")] on every public API
    // already ensures Release builds have zero call sites, so the static ctor
    // never fires and no helper is spawned. In Debug builds — `dotnet run`,
    // `dotnet test`, VS F5, Rider Debug alike — the viewer starts on first
    // touch. One source of truth; no #if DEBUG anywhere else.
    //
    // The viewer runs in a SEPARATE OS PROCESS (IfcEnvelopeMapper.DebugServer.exe),
    // not in-process. Rationale: a .NET debugger attached to this process freezes
    // ALL managed threads on a breakpoint, including an in-process HttpListener —
    // so the browser would stall at "Pending…" for the entire pause. A separate
    // OS process has its own thread scheduler that the debugger has no authority
    // over, so the HTTP server keeps responding while the CLI is paused.
    //
    // Wrapped in try/catch: type-initializer exceptions are sticky — they
    // permanently brick the type for the process lifetime. A failed helper
    // launch shouldn't take the ability to log shapes down with it.
    static GeometryDebug()
    {
        try
        {
            // The helper DLL and debug-viewer/ live next to Debug.dll, flowed
            // in via MSBuild Content items from the DebugServer project.
            // We launch it as `dotnet IfcEnvelopeMapper.DebugServer.dll ...`
            // (no native apphost — GDrive Streaming intermittently blocks
            // apphost .exe emission with "user-mapped section open").
            var helperDll  = Path.Combine(AppContext.BaseDirectory, "IfcEnvelopeMapper.DebugServer.dll");
            var viewerHtml = Path.Combine(AppContext.BaseDirectory, "debug-viewer", "index.html");

            if (!File.Exists(helperDll) || !File.Exists(viewerHtml))
            {
                Console.Error.WriteLine(
                    $"[GeometryDebug] viewer skipped — missing helper or HTML next to Debug.dll " +
                    $"(helperDll={File.Exists(helperDll)}, html={File.Exists(viewerHtml)})");
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath)!);

            var startInfo = new ProcessStartInfo
            {
                FileName               = "dotnet",
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true,
            };
            startInfo.ArgumentList.Add(helperDll);
            startInfo.ArgumentList.Add("5173");
            startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
            startInfo.ArgumentList.Add(viewerHtml);
            startInfo.ArgumentList.Add(OutputPath);

            _helperProcess = Process.Start(startInfo);
            if (_helperProcess is null)
            {
                Console.Error.WriteLine("[GeometryDebug] Process.Start returned null");
                return;
            }

            // Pump helper stdout/stderr into this console so the "Debug viewer:
            // http://localhost:PORT/" line and any errors surface to the user.
            _helperProcess.OutputDataReceived += (_, e) => { if (e.Data is not null) Console.WriteLine(e.Data); };
            _helperProcess.ErrorDataReceived  += (_, e) => { if (e.Data is not null) Console.Error.WriteLine(e.Data); };
            _helperProcess.BeginOutputReadLine();
            _helperProcess.BeginErrorReadLine();

            // Graceful shutdown. The helper also has a parent-PID watchdog as
            // backstop for hard crashes where ProcessExit never fires.
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                try
                {
                    if (_helperProcess is { HasExited: false })
                    {
                        _helperProcess.Kill();
                    }
                }
                catch { /* best-effort cleanup */ }
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[GeometryDebug] viewer start failed: {ex.Message}");
        }
    }

    // ── High-level shape API (all [Conditional("DEBUG")]) ───────────────────

    [Conditional("DEBUG")]
    public static void Mesh(DMesh3 mesh, string color = "#ff0000", string label = "")
    {
        Add(new MeshShape(mesh, color, label));
    }

    // Batched: collapses N meshes into one MeshShape so the viewer shows one
    // layer per call (not N identical buttons when emitting a per-group layer).
    [Conditional("DEBUG")]
    public static void Meshes(IEnumerable<DMesh3> meshes, string color = "#cccccc", string label = "")
    {
        Add(new MeshShape(Merge(meshes), color, label));
    }

    // Per-element emission: each call becomes its own glTF node tagged with
    // { globalId, ifcType } in extras. The viewer groups nodes by ifcType for
    // layer buttons but can raycast/highlight individual elements by globalId.
    // Callers pass raw fields (not BuildingElement) to keep this project
    // decoupled from IfcEnvelopeMapper.Core.
    [Conditional("DEBUG")]
    public static void Element(DMesh3 mesh, string globalId, string ifcType, string color = "#cccccc")
    {
        Add(new MeshShape(mesh, color, ifcType, globalId));
    }

    // Slice of a mesh by triangle IDs — extracts just those tris into a new DMesh3.
    [Conditional("DEBUG")]
    public static void Triangles(DMesh3 mesh, IEnumerable<int> triangleIds,
                                  string color = "#ff0000", string label = "")
    {
        Add(new MeshShape(ExtractTriangles(mesh, triangleIds), color, label));
    }

    [Conditional("DEBUG")]
    public static void Voxels(VoxelGrid3D grid, IEnumerable<VoxelCoord> coords,
                               string color = "#0000ff", string label = "")
    {
        Add(new MeshShape(GeometricPrimitives.VoxelCubes(grid, coords, shrinkFactor: 0.25), color, label));
    }

    [Conditional("DEBUG")]
    public static void Points(IEnumerable<Vector3d> points, string color = "#ffff00", string label = "")
    {
        Add(new PointsShape(points.ToArray(), color, label));
    }

    [Conditional("DEBUG")]
    public static void Line(Vector3d from, Vector3d to, string color = "#ffffff", string label = "")
    {
        Add(new LinesShape([(from, to)], color, label));
    }

    [Conditional("DEBUG")]
    public static void Lines(IEnumerable<(Vector3d From, Vector3d To)> segments,
                              string color = "#ffffff", string label = "")
    {
        Add(new LinesShape(segments.ToArray(), color, label));
    }

    [Conditional("DEBUG")]
    public static void Box(AxisAlignedBox3d box, string color = "#00ffff", string label = "")
    {
        Add(new LinesShape(GeometricPrimitives.BoxWireframe(box).ToArray(), color, label));
    }

    [Conditional("DEBUG")]
    public static void Plane(Plane3d plane, double displaySize = 1.0,
                              string color = "#00ff00", string label = "")
    {
        Add(new MeshShape(GeometricPrimitives.PlaneQuad(plane, displaySize), color, label));
    }

    [Conditional("DEBUG")]
    public static void Sphere(Vector3d center, double radius,
                               string color = "#ff00ff", string label = "")
    {
        Add(new MeshShape(GeometricPrimitives.Sphere(center, radius), color, label));
    }

    [Conditional("DEBUG")]
    public static void Normal(Vector3d origin, Vector3d direction, double length = 0.5,
                               string color = "#ffff00", string label = "")
    {
        Add(new LinesShape([(origin, origin + direction * length)], color, label));
    }

    // Writes a sidecar JSON { voxelSize, origin, occupants: { "x,y,z": [...ids] } }
    // next to the GLB so the viewer can reverse a picked voxel back to the
    // BuildingElements that rasterized into it. Only cells with ≥1 occupant
    // are emitted (sparse); typical file is small (few hundred KB even for
    // 10k voxels × ~2 ids).
    //
    // Atomic write: same tmp-then-Move pattern as GLB emission — the helper
    // reads with FileShare.ReadWrite | FileShare.Delete so a mid-flight rename
    // does not deny the next poll.
    [Conditional("DEBUG")]
    public static void VoxelOccupants(VoxelGrid3D grid)
    {
        var occupants = new Dictionary<string, string[]>();
        for (var x = 0; x < grid.NX; x++)
        {
            for (var y = 0; y < grid.NY; y++)
            {
                for (var z = 0; z < grid.NZ; z++)
                {
                    var coord = new VoxelCoord(x, y, z);
                    var ids   = grid.OccupantsOf(coord);
                    if (ids.Count == 0)
                    {
                        continue;
                    }
                    occupants[$"{x},{y},{z}"] = ids.ToArray();
                }
            }
        }

        var payload = new
        {
            voxelSize = grid.VoxelSize,
            origin    = new[] { grid.Bounds.Min.x, grid.Bounds.Min.y, grid.Bounds.Min.z },
            nx        = grid.NX,
            ny        = grid.NY,
            nz        = grid.NZ,
            occupants,
        };

        var tmp = OccupantsPath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(payload));
        GltfSerializer.MoveWithRetry(tmp, OccupantsPath);
    }

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
}
