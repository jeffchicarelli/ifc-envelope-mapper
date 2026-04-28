using System.Diagnostics;
using g4;
using IfcEnvelopeMapper.Core.Extensions;
using IfcEnvelopeMapper.Core.Domain.Voxel;
using IfcEnvelopeMapper.Ifc.Domain;

namespace IfcEnvelopeMapper.Engine.Visualization.Api;

/// <summary>
/// Geometric debugger for algorithm development. A pure facade: every public
/// method is <c>[Conditional("DEBUG")]</c>, so callers compiled without
/// <c>DEBUG</c> emit zero IL at the call site — Release builds pay nothing,
/// with no wrappers needed.
///
/// One overloaded <c>Send</c> covers every supported shape — the compiler
/// dispatches on argument type. Each method builds a <see cref="DebugShape"/>
/// and hands it to <see cref="Scene"/>; <see cref="ViewerHelper"/> is
/// contacted (idempotently) on the first emission to spawn the helper viewer
/// process. Open <c>http://localhost:5173/</c> in a browser: the viewer
/// polls the GLB 5×/sec and reflects the current geometric state. Set an
/// IDE breakpoint after a call — the file is already written by the time
/// execution pauses.
/// </summary>
public static class GeometryDebug
{
    /// <summary>
    /// Runtime kill-switch. When <c>false</c>, every emission becomes a no-op
    /// even in Debug builds. The CLI sets this to <c>false</c> at startup so
    /// production runs never spawn the viewer helper or write GLBs; xunit
    /// tests keep the default (<c>true</c>) so they continue to produce
    /// per-test disagreement GLBs.
    /// </summary>
    public static bool Enabled { get; set; } = true;

    /// <summary>
    /// Redirects GLB output to <paramref name="outputPath"/>. Tests call this
    /// before any other <c>GeometryDebug.*</c> method to isolate artefacts on
    /// disk and typically pass <c>launchServer: false</c> to skip spawning the
    /// viewer helper process. The CLI never needs this — it sets
    /// <see cref="Enabled"/>=<c>false</c> at startup instead.
    /// </summary>
    [Conditional("DEBUG")]
    public static void Configure(string outputPath, bool launchServer = true)
    {
        Scene.Configure(outputPath, launchServer);
    }

    // ── Mesh emissions ──────────────────────────────────────────────────────

    [Conditional("DEBUG")]
    public static void Send(DMesh3 mesh, Color? color = null, string label = "")
    {
        Add(new MeshShape(mesh, color ?? Color.Red, label));
    }

    // Batched: collapses N meshes into one MeshShape so the viewer shows one
    // layer per call (not N identical buttons when emitting a per-group layer).
    [Conditional("DEBUG")]
    public static void Send(IEnumerable<DMesh3> meshes, Color? color = null, string label = "")
    {
        Add(new MeshShape(meshes.Merge(), color ?? Color.Gray, label));
    }

    // Slice of a mesh by triangle IDs — extracts just those tris into a new DMesh3.
    [Conditional("DEBUG")]
    public static void Send(DMesh3 mesh, IEnumerable<int> triangleIds,
                             Color? color = null, string label = "")
    {
        Add(new MeshShape(mesh.ExtractTriangles(triangleIds), color ?? Color.Red, label));
    }

    // ── Voxel emissions ─────────────────────────────────────────────────────

    [Conditional("DEBUG")]
    public static void Send(VoxelGrid3D grid, IEnumerable<VoxelCoord> coords,
                             Color? color = null, string label = "")
    {
        Add(new MeshShape(grid.CubesAt(coords, shrinkFactor: 0.25), color ?? Color.Blue, label));
    }

    // Convenience for the common "show all voxels in state X" pattern. Default
    // colours match the existing semantic palette (occupied = translucent green,
    // exterior = translucent blue, interior = translucent red, void = neutral).
    //
    // <paramref name="shell"/> defaults to true: only voxels with at least one
    // neighbour in a different state are emitted. The interior of a solid block
    // is invisible to the camera anyway, and shell-only drops voxel count by
    // ~10× on typical grids (surface of an N-voxel solid scales as N^(2/3)).
    // Pass <c>shell: false</c> for "every cube" runs (thesis figures, full-set
    // verification) — at the cost of much larger emissions.
    [Conditional("DEBUG")]
    public static void Send(VoxelGrid3D grid, VoxelState state, Color? color = null)
    {
        IEnumerable<VoxelCoord> coords = grid.VoxelsByState(state);
        var coordList = coords.ToList();

        var defaultColor = state switch
        {
            VoxelState.Occupied => Color.FromHex("#00aa00c0"),
            VoxelState.Exterior => Color.FromHex("#0055ffc0"),
            VoxelState.Interior => Color.FromHex("#ff0000c0"),
            VoxelState.Void     => Color.FromHex("#ccccccc0"),
            _                   => Color.White,
        };
        Add(new MeshShape(grid.CubesAt(coordList, shrinkFactor: 0.25),
                          color ?? defaultColor, state.ToString().ToLowerInvariant()));
    }

    // ── Geometric primitives ────────────────────────────────────────────────

    [Conditional("DEBUG")]
    public static void Send(AxisAlignedBox3d box, Color? color = null, string label = "")
    {
        Add(new LinesShape(box.ToWireframe().ToArray(), color ?? Color.Cyan, label));
    }

    [Conditional("DEBUG")]
    public static void Send(Plane3d plane, double displaySize = 1.0,
                             Color? color = null, string label = "")
    {
        Add(new MeshShape(plane.ToQuadMesh(displaySize), color ?? Color.Green, label));
    }

    // Sphere — disambiguated from Send(Vector3d, Vector3d) by the second
    // parameter type (double vs Vector3d).
    [Conditional("DEBUG")]
    public static void Send(Vector3d center, double radius,
                             Color? color = null, string label = "")
    {
        Add(new MeshShape(center.ToSphere(radius), color ?? Color.Magenta, label));
    }

    // Line. Normals (origin + direction*length) are caller-computed:
    //   Send(origin, origin + direction * length, Color.Yellow, "normal")
    [Conditional("DEBUG")]
    public static void Send(Vector3d from, Vector3d to, Color? color = null, string label = "")
    {
        Add(new LinesShape([(from, to)], color ?? Color.White, label));
    }

    [Conditional("DEBUG")]
    public static void Send(IEnumerable<(Vector3d From, Vector3d To)> segments,
                             Color? color = null, string label = "")
    {
        Add(new LinesShape(segments.ToArray(), color ?? Color.White, label));
    }

    [Conditional("DEBUG")]
    public static void Send(IEnumerable<Vector3d> points, Color? color = null, string label = "")
    {
        Add(new PointsShape(points.ToArray(), color ?? Color.Yellow, label));
    }

    // ── IFC element emissions ───────────────────────────────────────────────

    // Per-element emission: each call becomes its own glTF node tagged with
    // { globalId, ifcType } in extras. The viewer groups nodes by ifcType
    // for layer buttons and can raycast/highlight individual elements by
    // globalId. Color falls through to the IFC-type palette when omitted.
    [Conditional("DEBUG")]
    public static void Send(Element element, Color? color = null)
    {
        Add(new MeshShape(
            element.GetMesh(),
            color ?? IfcTypePalette.For(element.IfcType),
            element.IfcType,
            element.GlobalId));
    }

    // Batch: each element gets its palette colour. Buffers all shapes and
    // flushes once — drops the per-loop encode cost from O(N²) to O(N).
    [Conditional("DEBUG")]
    public static void Send(IEnumerable<Element> elements)
    {
        if (!Enabled)
        {
            return;
        }

        ViewerHelper.EnsureStarted(Scene.OutputPath, Scene.LaunchServer);
        foreach (var element in elements)
        {
            Scene.AddNoFlush(new MeshShape(
                element.GetMesh(),
                IfcTypePalette.For(element.IfcType),
                element.IfcType,
                element.GlobalId));
        }
        Scene.Flush();
    }

    // Batch with a single colour override (e.g. "highlight all exterior
    // elements in magenta"). Same single-flush optimisation as the
    // palette overload.
    [Conditional("DEBUG")]
    public static void Send(IEnumerable<Element> elements, Color color)
    {
        if (!Enabled)
        {
            return;
        }

        ViewerHelper.EnsureStarted(Scene.OutputPath, Scene.LaunchServer);
        foreach (var element in elements)
        {
            Scene.AddNoFlush(new MeshShape(
                element.GetMesh(), color, element.IfcType, element.GlobalId));
        }
        Scene.Flush();
    }

    // ── Lifecycle ───────────────────────────────────────────────────────────

    [Conditional("DEBUG")]
    public static void Clear()
    {
        if (!Enabled)
        {
            return;
        }

        ViewerHelper.EnsureStarted(Scene.OutputPath, Scene.LaunchServer);
        Scene.Clear();
    }

    // Force the GLB on disk up to date with the current buffer. Bypasses
    // the Add throttle. Call at the end of a test or at a breakpoint
    // when you need the very latest state visible to the viewer.
    [Conditional("DEBUG")]
    public static void Flush()
    {
        if (!Enabled)
        {
            return;
        }

        Scene.Flush();
    }

    // Single internal entry that gates on Enabled, ensures the viewer helper
    // is up (idempotent), and hands the shape to Scene. Keeps each public
    // overload to one line.
    private static void Add(DebugShape shape)
    {
        if (!Enabled)
        {
            return;
        }

        ViewerHelper.EnsureStarted(Scene.OutputPath, Scene.LaunchServer);
        Scene.Add(shape);
    }
}
