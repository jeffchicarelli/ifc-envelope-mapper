using System.Diagnostics;
using g4;
using IfcEnvelopeMapper.Core.Extensions;
using IfcEnvelopeMapper.Core.Domain.Voxel;

namespace IfcEnvelopeMapper.Engine.Debug.Api;

/// <summary>
/// Geometric debugger for algorithm development. A pure facade: every public
/// method is <c>[Conditional("DEBUG")]</c>, so callers compiled without
/// <c>DEBUG</c> emit zero IL at the call site — Release builds pay nothing,
/// with no wrappers needed.
///
/// Each method builds a <see cref="DebugShape"/> and hands it to
/// <see cref="DebugSession"/>, which owns the shape list and the viewer
/// helper-process lifecycle. Open <c>http://localhost:5173/</c> in a browser:
/// the viewer polls the GLB 5×/sec and reflects the current geometric state.
/// Set an IDE breakpoint after a call — the file is already written by the
/// time execution pauses.
/// </summary>
public static class GeometryDebug
{
    /// <summary>
    /// Runtime kill-switch. When <c>false</c>, every <see cref="DebugSession"/>
    /// emission becomes a no-op even in Debug builds. The CLI sets this to
    /// <c>false</c> at startup so production runs never spawn the viewer helper
    /// or write GLBs; xunit tests keep the default (<c>true</c>) so they
    /// continue to produce per-test disagreement GLBs.
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
        DebugSession.Configure(outputPath, launchServer);
    }

    // ── High-level shape API (all [Conditional("DEBUG")]) ───────────────────

    [Conditional("DEBUG")]
    public static void Mesh(DMesh3 mesh, Color? color = null, string label = "")
    {
        DebugSession.Add(new MeshShape(mesh, color ?? Color.Red, label));
    }

    // Batched: collapses N meshes into one MeshShape so the viewer shows one
    // layer per call (not N identical buttons when emitting a per-group layer).
    [Conditional("DEBUG")]
    public static void Meshes(IEnumerable<DMesh3> meshes, Color? color = null, string label = "")
    {
        DebugSession.Add(new MeshShape(meshes.Merge(), color ?? Color.Gray, label));
    }

    // Per-element emission: each call becomes its own glTF node tagged with
    // { globalId, ifcType } in extras. The viewer groups nodes by ifcType for
    // layer buttons but can raycast/highlight individual elements by globalId.
    // Callers pass raw fields (not Element) to keep this project
    // decoupled from IfcEnvelopeMapper.Core.
    [Conditional("DEBUG")]
    public static void Element(DMesh3 mesh, string globalId, string ifcType, Color? color = null)
    {
        DebugSession.Add(new MeshShape(mesh, color ?? Color.Gray, ifcType, globalId));
    }

    // Slice of a mesh by triangle IDs — extracts just those tris into a new DMesh3.
    [Conditional("DEBUG")]
    public static void Triangles(DMesh3 mesh, IEnumerable<int> triangleIds,
                                  Color? color = null, string label = "")
    {
        DebugSession.Add(new MeshShape(mesh.ExtractTriangles(triangleIds), color ?? Color.Red, label));
    }

    [Conditional("DEBUG")]
    public static void Voxels(VoxelGrid3D grid, IEnumerable<VoxelCoord> coords,
                               Color? color = null, string label = "")
    {
        DebugSession.Add(new MeshShape(grid.CubesAt(coords, shrinkFactor: 0.25), color ?? Color.Blue, label));
    }

    [Conditional("DEBUG")]
    public static void Points(IEnumerable<Vector3d> points, Color? color = null, string label = "")
    {
        DebugSession.Add(new PointsShape(points.ToArray(), color ?? Color.Yellow, label));
    }

    [Conditional("DEBUG")]
    public static void Line(Vector3d from, Vector3d to, Color? color = null, string label = "")
    {
        DebugSession.Add(new LinesShape([(from, to)], color ?? Color.White, label));
    }

    [Conditional("DEBUG")]
    public static void Lines(IEnumerable<(Vector3d From, Vector3d To)> segments,
                              Color? color = null, string label = "")
    {
        DebugSession.Add(new LinesShape(segments.ToArray(), color ?? Color.White, label));
    }

    [Conditional("DEBUG")]
    public static void Box(AxisAlignedBox3d box, Color? color = null, string label = "")
    {
        DebugSession.Add(new LinesShape(box.ToWireframe().ToArray(), color ?? Color.Cyan, label));
    }

    [Conditional("DEBUG")]
    public static void Plane(Plane3d plane, double displaySize = 1.0,
                              Color? color = null, string label = "")
    {
        DebugSession.Add(new MeshShape(plane.ToQuadMesh(displaySize), color ?? Color.Green, label));
    }

    [Conditional("DEBUG")]
    public static void Sphere(Vector3d center, double radius,
                               Color? color = null, string label = "")
    {
        DebugSession.Add(new MeshShape(center.ToSphere(radius), color ?? Color.Magenta, label));
    }

    [Conditional("DEBUG")]
    public static void Normal(Vector3d origin, Vector3d direction, double length = 0.5,
                               Color? color = null, string label = "")
    {
        DebugSession.Add(new LinesShape([(origin, origin + direction * length)], color ?? Color.Yellow, label));
    }

    [Conditional("DEBUG")]
    public static void Clear()
    {
        DebugSession.Clear();
    }

}
