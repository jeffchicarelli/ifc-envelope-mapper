using System.Numerics;
using System.Text.Json.Nodes;
using g4;

using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;

using IfcEnvelopeMapper.Engine.Debug.Api;

namespace IfcEnvelopeMapper.Engine.Debug.Serialization;

// Encodes a list of DebugShape payloads into a single GLB file.
//
// Kept internal: only the Debug facade consumes it today, and the API
// (switch over a closed set of payloads) would be wrong as a public surface.
// Promote when there's a real non-debug consumer.
//
// Only three payload kinds — Mesh / Lines / Points — matching the glTF
// primitive-topology set {TRIANGLES, LINES, POINTS}. All higher-level
// shapes (boxes, spheres, voxels, normals) are pre-converted upstream by
// GeometryDebug using GeometricPrimitives.
internal static class GltfSerializer
{
    public static void Flush(List<DebugShape> shapes, string outputPath)
    {
        // Group by (kind, label, color) so repeated calls to GeometryDebug with the
        // same identity merge into one GLB mesh → one layer button in the viewer.
        // Different colors under the same label stay split (the SharpGLTF/three.js
        // loader will auto-suffix), which is the user-chosen policy: same color =
        // semantically the same layer; a color change implies intent.
        //
        // Insertion order is preserved (Dictionary<TKey,TValue> iterates in insertion
        // order in the current runtime), so layer buttons appear in the order the
        // first call with each (label, color) arrived.
        var meshes   = new Dictionary<(string label, string color), DMesh3>();

        // Per-element emissions (MeshShape with GlobalId) skip the merge path so
        // the viewer gets one node per element with { globalId, ifcType } extras.
        var elements = new List<MeshShape>();
        var lines    = new Dictionary<(string label, string color), List<(Vector3d From, Vector3d To)>>();
        var points   = new Dictionary<(string label, string color), List<Vector3d>>();

        foreach (var shape in shapes)
        {
            switch (shape)
            {
                case MeshShape { GlobalId: not null } m:
                {
                    elements.Add(m);
                    break;
                }

                case MeshShape m:
                {
                    var key = (m.Label, m.Color);
                    if (!meshes.TryGetValue(key, out var acc))
                    {
                        acc = new DMesh3();
                        meshes[key] = acc;
                    }

                    AppendMesh(acc, m.Mesh);
                    break;
                }

                case LinesShape l:
                {
                    var key = (l.Label, l.Color);
                    if (!lines.TryGetValue(key, out var acc))
                    {
                        acc = new List<(Vector3d, Vector3d)>();
                        lines[key] = acc;
                    }

                    acc.AddRange(l.Segments);
                    break;
                }

                case PointsShape p:
                {
                    var key = (p.Label, p.Color);
                    if (!points.TryGetValue(key, out var acc))
                    {
                        acc = new List<Vector3d>();
                        points[key] = acc;
                    }

                    acc.AddRange(p.Points);
                    break;
                }
            }
        }

        var scene = new SceneBuilder();
        foreach (var ((label, color), mesh) in meshes)
        {
            AddMesh(scene, mesh, color, label);
        }

        foreach (var m in elements)
        {
            AddElementMesh(scene, m);
        }

        foreach (var ((label, color), segs) in lines)
        {
            AddLines(scene, segs.ToArray(), color, label);
        }

        foreach (var ((label, color), pts) in points)
        {
            AddPoints(scene, pts.ToArray(), color, label);
        }

        // SaveGLB writes a single self-contained binary file (JSON + buffers embedded).
        // SaveGLTF would produce a split .gltf + .bin pair, which the browser-based
        // debug-viewer cannot resolve via showOpenFilePicker (only one file per pick).
        //
        // Atomic-write pattern: write to .tmp, then rename. File.Move within one
        // filesystem is an inode swap — the OS sees either the old file or the new
        // file, never a half-written one. The viewer polls every 1s; without this,
        // a fetch landing mid-SaveGLB reads a truncated GLB, loader.parse hangs,
        // and the status stays on "Starting…" indefinitely.
        var tmpPath = outputPath + ".tmp";
        scene.ToGltf2().SaveGLB(tmpPath);
        AtomicFile.MoveWithRetry(tmpPath, outputPath);
    }

    // Concatenates `source` triangles into `dest`. Uses the VertexCount offset
    // trick (safe because GeometricPrimitives.* meshes have no deletion holes —
    // vertex IDs are dense 0..VertexCount-1). Matches the pattern in
    // GeometryDebug.Merge.
    private static void AppendMesh(DMesh3 dest, DMesh3 source)
    {
        var offset = dest.VertexCount;
        for (var vid = 0; vid < source.MaxVertexID; vid++)
        {
            if (source.IsVertex(vid))
            {
                dest.AppendVertex(source.GetVertex(vid));
            }
        }

        for (var tid = 0; tid < source.MaxTriangleID; tid++)
        {
            if (!source.IsTriangle(tid))
            {
                continue;
            }

            var t = source.GetTriangle(tid);
            dest.AppendTriangle(new Index3i(t.a + offset, t.b + offset, t.c + offset));
        }
    }

    // ── Color helpers ────────────────────────────────────────────────────────

    private static Vector4 ParseColor(string hex)
    {
        hex = hex.TrimStart('#');
        var r = Convert.ToInt32(hex[0..2], 16) / 255f;
        var g = Convert.ToInt32(hex[2..4], 16) / 255f;
        var b = Convert.ToInt32(hex[4..6], 16) / 255f;
        var a = hex.Length == 8 ? Convert.ToInt32(hex[6..8], 16) / 255f : 1f;
        return new Vector4(r, g, b, a);
    }

    private static MaterialBuilder MakeMaterial(string color, string label)
    {
        var c = ParseColor(color);
        var mat = new MaterialBuilder(label)
            .WithDoubleSide(true)
            .WithUnlitShader() // Viewer replaces with MeshLambertMaterial on load — keep GLB flat.
            .WithBaseColor(c);
        return c.W < 1f ? mat.WithAlpha(AlphaMode.BLEND) : mat;
    }

    // ── Vertex helper ────────────────────────────────────────────────────────

    private static VertexPosition Vp(Vector3d v) => new((float)v.x, (float)v.y, (float)v.z);

    // ── Primitive adders ─────────────────────────────────────────────────────

    private static void AddMesh(SceneBuilder scene, DMesh3 mesh, string color, string label)
    {
        var mb   = new MeshBuilder<VertexPosition>(label);
        var prim = mb.UsePrimitive(MakeMaterial(color, label));

        for (var tid = 0; tid < mesh.MaxTriangleID; tid++)
        {
            if (!mesh.IsTriangle(tid))
            {
                continue;
            }

            var t = mesh.GetTriangle(tid);
            prim.AddTriangle(Vp(mesh.GetVertex(t.a)), Vp(mesh.GetVertex(t.b)), Vp(mesh.GetVertex(t.c)));
        }

        scene.AddRigidMesh(mb, Matrix4x4.Identity);
    }

    // Per-element node with glTF `extras = { globalId, ifcType }`. The viewer
    // reads node.userData from these extras for click-picking + grouping.
    // Label carries ifcType (set by GeometryDebug.Element) — re-copied into
    // extras so the viewer doesn't have to infer it from the node name.
    private static void AddElementMesh(SceneBuilder scene, MeshShape m)
    {
        var mb   = new MeshBuilder<VertexPosition>($"{m.Label}:{m.GlobalId}");
        var prim = mb.UsePrimitive(MakeMaterial(m.Color, m.Label));

        for (var tid = 0; tid < m.Mesh.MaxTriangleID; tid++)
        {
            if (!m.Mesh.IsTriangle(tid))
            {
                continue;
            }

            var t = m.Mesh.GetTriangle(tid);
            prim.AddTriangle(Vp(m.Mesh.GetVertex(t.a)), Vp(m.Mesh.GetVertex(t.b)), Vp(m.Mesh.GetVertex(t.c)));
        }

        var instance = scene.AddRigidMesh(mb, Matrix4x4.Identity);

        // InstanceBuilder.Extras is read-only; the writable one lives on the
        // transformer (RigidTransformer inherits ContentTransformer.Extras),
        // typed as JsonNode (SharpGLTF alpha0032+ migrated off its own JsonContent).
        instance.Content.Extras = new JsonObject
        {
            ["globalId"] = m.GlobalId,
            ["ifcType"]  = m.Label,
        };
    }

    private static void AddLines(SceneBuilder scene, (Vector3d From, Vector3d To)[] segs,
                                  string color, string label)
    {
        var mb   = new MeshBuilder<VertexPosition>(label);
        var prim = mb.UsePrimitive(MakeMaterial(color, label), 2); // 2 vertices/primitive = LINES

        foreach (var (a, b) in segs)
        {
            prim.AddLine(Vp(a), Vp(b));
        }

        scene.AddRigidMesh(mb, Matrix4x4.Identity);
    }

    private static void AddPoints(SceneBuilder scene, Vector3d[] points,
                                   string color, string label)
    {
        var mb   = new MeshBuilder<VertexPosition>(label);
        var prim = mb.UsePrimitive(MakeMaterial(color, label), 1); // 1 vertex/primitive = POINTS

        foreach (var p in points)
        {
            prim.AddPoint(Vp(p));
        }

        scene.AddRigidMesh(mb, Matrix4x4.Identity);
    }
}
