using System.Diagnostics;
using g4;
using IfcEnvelopeMapper.Domain.Extensions;
using IfcEnvelopeMapper.Domain.Interfaces;
using IfcEnvelopeMapper.Domain.Voxel;

namespace IfcEnvelopeMapper.Infrastructure.Visualization.Api;

/// <summary>
/// Geometric debugger for algorithm development. A pure facade: every public method is <c>[Conditional("DEBUG")]</c>, so callers compiled
/// without <c>DEBUG</c> emit zero IL at the call site — Release builds pay nothing, with no wrappers needed. One overloaded <c>Send</c> covers every
/// supported shape — the compiler dispatches on argument type. Each method builds a <see cref="DebugShape"/> and hands it to <see cref="Scene"/>;
/// <see cref="ViewerHelper"/> is contacted (idempotently) on the first emission to spawn the helper viewer process. Open
/// <c>http://localhost:5173/</c> in a browser: the viewer polls the GLB 5×/sec and reflects the current geometric state. Set an IDE breakpoint after
/// a call — the file is already written by the time execution pauses.
/// </summary>
public static class GeometryDebug
{
    /// <summary>Runtime kill-switch. When <c>false</c>, every <c>Send</c> overload is a no-op even in Debug builds.</summary>
    public static bool Enabled { get; set; } = true;

    /// <summary>Redirects GLB output to <paramref name="outputPath"/>. Pass <c>launchServer: false</c> to suppress the viewer-helper process.</summary>
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

    [Conditional("DEBUG")]
    public static void Send(IEnumerable<DMesh3> meshes, Color? color = null, string label = "")
    {
        Add(new MeshShape(meshes.Merge(), color ?? Color.Gray, label));
    }

    [Conditional("DEBUG")]
    public static void Send(DMesh3 mesh, IEnumerable<int> triangleIds, Color? color = null, string label = "")
    {
        Add(new MeshShape(mesh.ExtractTriangles(triangleIds), color ?? Color.Red, label));
    }

    // ── Voxel emissions ─────────────────────────────────────────────────────

    [Conditional("DEBUG")]
    public static void Send(VoxelGrid3D grid, IEnumerable<VoxelCoord> coords, Color? color = null, string label = "")
    {
        Add(new MeshShape(grid.CubesAt(coords, 0.25), color ?? Color.Blue, label));
    }

    [Conditional("DEBUG")]
    public static void Send(VoxelGrid3D grid, VoxelState state, Color? color = null)
    {
        var coords = grid.VoxelsByState(state);
        var coordList = coords.ToList();

        var defaultColor = state switch
        {
            VoxelState.Occupied => Color.FromHex("#00aa00c0"),
            VoxelState.Exterior => Color.FromHex("#0055ffc0"),
            VoxelState.Interior => Color.FromHex("#ff0000c0"),
            VoxelState.Void     => Color.FromHex("#ccccccc0"),
            _                   => Color.White
        };

        Add(new MeshShape(grid.CubesAt(coordList, 0.25), color ?? defaultColor, state.ToString().ToLowerInvariant()));
    }

    // ── Geometric primitives ────────────────────────────────────────────────

    [Conditional("DEBUG")]
    public static void Send(AxisAlignedBox3d box, Color? color = null, string label = "")
    {
        Add(new LinesShape(box.ToWireframe().ToArray(), color ?? Color.Cyan, label));
    }

    [Conditional("DEBUG")]
    public static void Send(Plane3d plane, double displaySize = 1.0, Color? color = null, string label = "")
    {
        Add(new MeshShape(plane.ToQuadMesh(displaySize), color ?? Color.Green, label));
    }

    [Conditional("DEBUG")]
    public static void Send(Vector3d center, double radius, Color? color = null, string label = "")
    {
        Add(new MeshShape(center.ToSphere(radius), color ?? Color.Magenta, label));
    }

    [Conditional("DEBUG")]
    public static void Send(Vector3d from, Vector3d to, Color? color = null, string label = "")
    {
        Add(new LinesShape([(from, to)], color ?? Color.White, label));
    }

    [Conditional("DEBUG")]
    public static void Send(IEnumerable<(Vector3d From, Vector3d To)> segments, Color? color = null, string label = "")
    {
        Add(new LinesShape(segments.ToArray(), color ?? Color.White, label));
    }

    [Conditional("DEBUG")]
    public static void Send(IEnumerable<Vector3d> points, Color? color = null, string label = "")
    {
        Add(new PointsShape(points.ToArray(), color ?? Color.Yellow, label));
    }

    // ── IFC element emissions ───────────────────────────────────────────────

    [Conditional("DEBUG")]
    public static void Send(IElement element, Color? color = null)
    {
        Add(new MeshShape(element.GetMesh(), color ?? IfcTypePalette.For(element.IfcType), element.IfcType, element.GlobalId));
    }

    [Conditional("DEBUG")]
    public static void Send(IEnumerable<IElement> elements)
    {
        if (!Enabled)
        {
            return;
        }

        ViewerHelper.EnsureStarted(Scene.OutputPath, Scene.LaunchServer);

        foreach (var element in elements)
        {
            Scene.AddNoFlush(new MeshShape(element.GetMesh(), IfcTypePalette.For(element.IfcType), element.IfcType, element.GlobalId));
        }

        Scene.Flush();
    }

    [Conditional("DEBUG")]
    public static void Send(IEnumerable<IElement> elements, Color color)
    {
        if (!Enabled)
        {
            return;
        }

        ViewerHelper.EnsureStarted(Scene.OutputPath, Scene.LaunchServer);

        foreach (var element in elements)
        {
            Scene.AddNoFlush(new MeshShape(element.GetMesh(), color, element.IfcType, element.GlobalId));
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

    [Conditional("DEBUG")]
    public static void Flush()
    {
        if (!Enabled)
        {
            return;
        }

        Scene.Flush();
    }

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
