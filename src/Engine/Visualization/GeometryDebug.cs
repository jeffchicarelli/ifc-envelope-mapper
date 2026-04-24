using System.Diagnostics;
using g4;
using IfcEnvelopeMapper.Core.Extensions;
using IfcEnvelopeMapper.Core.Domain.Voxel;

namespace IfcEnvelopeMapper.Engine.Visualization;

// Geometric debugger for algorithm development. Pure facade: every public
// method is [Conditional("DEBUG")] so callers compiled without DEBUG emit
// zero IL at the call site. Release builds pay nothing — no wrappers needed.
//
// Each method builds a DebugShape and hands it to DebugSession, which owns
// the shape list + helper process lifecycle. Open http://localhost:5173/
// in a browser — the viewer polls the GLB 5×/sec and reflects the current
// geometric state. Place an IDE breakpoint on the line after a call; the
// file is already written by the time execution pauses.
public static class GeometryDebug
{
    // Tests call this before any other GeometryDebug.* method to redirect GLB
    // output to a per-test path and (typically) skip spawning the viewer helper
    // process — they want isolated artefacts on disk, not a live browser viewer.
    // CLI never needs this; the default path + helper launch are correct for it.
    [Conditional("DEBUG")]
    public static void Configure(string outputPath, bool launchServer = true)
    {
        DebugSession.Configure(outputPath, launchServer);
    }

    // ── High-level shape API (all [Conditional("DEBUG")]) ───────────────────

    [Conditional("DEBUG")]
    public static void Mesh(DMesh3 mesh, string color = "#ff0000", string label = "")
    {
        DebugSession.Add(new MeshShape(mesh, color, label));
    }

    // Batched: collapses N meshes into one MeshShape so the viewer shows one
    // layer per call (not N identical buttons when emitting a per-group layer).
    [Conditional("DEBUG")]
    public static void Meshes(IEnumerable<DMesh3> meshes, string color = "#cccccc", string label = "")
    {
        DebugSession.Add(new MeshShape(meshes.Merge(), color, label));
    }

    // Per-element emission: each call becomes its own glTF node tagged with
    // { globalId, ifcType } in extras. The viewer groups nodes by ifcType for
    // layer buttons but can raycast/highlight individual elements by globalId.
    // Callers pass raw fields (not BuildingElement) to keep this project
    // decoupled from IfcEnvelopeMapper.Core.
    [Conditional("DEBUG")]
    public static void Element(DMesh3 mesh, string globalId, string ifcType, string color = "#cccccc")
    {
        DebugSession.Add(new MeshShape(mesh, color, ifcType, globalId));
    }

    // Slice of a mesh by triangle IDs — extracts just those tris into a new DMesh3.
    [Conditional("DEBUG")]
    public static void Triangles(DMesh3 mesh, IEnumerable<int> triangleIds,
                                  string color = "#ff0000", string label = "")
    {
        DebugSession.Add(new MeshShape(mesh.ExtractTriangles(triangleIds), color, label));
    }

    [Conditional("DEBUG")]
    public static void Voxels(VoxelGrid3D grid, IEnumerable<VoxelCoord> coords,
                               string color = "#0000ff", string label = "")
    {
        DebugSession.Add(new MeshShape(grid.CubesAt(coords, shrinkFactor: 0.25), color, label));
    }

    [Conditional("DEBUG")]
    public static void Points(IEnumerable<Vector3d> points, string color = "#ffff00", string label = "")
    {
        DebugSession.Add(new PointsShape(points.ToArray(), color, label));
    }

    [Conditional("DEBUG")]
    public static void Line(Vector3d from, Vector3d to, string color = "#ffffff", string label = "")
    {
        DebugSession.Add(new LinesShape([(from, to)], color, label));
    }

    [Conditional("DEBUG")]
    public static void Lines(IEnumerable<(Vector3d From, Vector3d To)> segments,
                              string color = "#ffffff", string label = "")
    {
        DebugSession.Add(new LinesShape(segments.ToArray(), color, label));
    }

    [Conditional("DEBUG")]
    public static void Box(AxisAlignedBox3d box, string color = "#00ffff", string label = "")
    {
        DebugSession.Add(new LinesShape(box.ToWireframe().ToArray(), color, label));
    }

    [Conditional("DEBUG")]
    public static void Plane(Plane3d plane, double displaySize = 1.0,
                              string color = "#00ff00", string label = "")
    {
        DebugSession.Add(new MeshShape(plane.ToQuadMesh(displaySize), color, label));
    }

    [Conditional("DEBUG")]
    public static void Sphere(Vector3d center, double radius,
                               string color = "#ff00ff", string label = "")
    {
        DebugSession.Add(new MeshShape(center.ToSphere(radius), color, label));
    }

    [Conditional("DEBUG")]
    public static void Normal(Vector3d origin, Vector3d direction, double length = 0.5,
                               string color = "#ffff00", string label = "")
    {
        DebugSession.Add(new LinesShape([(origin, origin + direction * length)], color, label));
    }

    [Conditional("DEBUG")]
    public static void Clear()
    {
        DebugSession.Clear();
    }

}
