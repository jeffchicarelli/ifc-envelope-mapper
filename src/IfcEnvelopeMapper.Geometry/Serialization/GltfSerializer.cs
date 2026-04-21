using System.Numerics;
using g4;
using IfcEnvelopeMapper.Geometry.Debug;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;

namespace IfcEnvelopeMapper.Geometry.Serialization;

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
        var scene = new SceneBuilder();
        foreach (var shape in shapes)
        {
            switch (shape)
            {
                case MeshShape m:   AddMesh(scene, m.Mesh, m.Color, m.Label);       break;
                case LinesShape l:  AddLines(scene, l.Segments, l.Color, l.Label);  break;
                case PointsShape p: AddPoints(scene, p.Points, p.Color, p.Label);   break;
            }
        }

        // SaveGLB writes a single self-contained binary file (JSON + buffers embedded).
        // SaveGLTF would produce a split .gltf + .bin pair, which the browser-based
        // debug-viewer cannot resolve via showOpenFilePicker (only one file per pick).
        scene.ToGltf2().SaveGLB(outputPath);
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

    private static void AddLines(SceneBuilder scene, (Vector3d From, Vector3d To)[] segs,
                                  string color, string label)
    {
        var mb   = new MeshBuilder<VertexPosition>(label);
        var prim = mb.UsePrimitive(MakeMaterial(color, label), 1); // 1 = LINES

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
        var prim = mb.UsePrimitive(MakeMaterial(color, label), 0); // 0 = POINTS

        foreach (var p in points)
        {
            prim.AddPoint(Vp(p));
        }

        scene.AddRigidMesh(mb, Matrix4x4.Identity);
    }
}
