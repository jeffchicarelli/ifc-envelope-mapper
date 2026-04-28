using System.Numerics;
using System.Text.Json.Nodes;
using g4;

using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;

using IfcEnvelopeMapper.Infrastructure.Visualization.Api;

namespace IfcEnvelopeMapper.Infrastructure.Visualization.Serialization;

// Encodes DebugShape payloads as glTF primitives attached to a long-lived
// SceneBuilder. Each Send becomes its own glTF node — no per-(label, color)
// merging — which lets the SceneBuilder accumulate incrementally across
// flushes. Per-shape AddTriangle iteration happens once (in AddShape, when
// the shape is first emitted), not on every flush. SaveGlb is the only
// per-flush cost — it serialises the existing SceneBuilder to disk.
//
// Kept internal: only Scene consumes it.
//
// Three payload kinds — Mesh / Lines / Points — matching the glTF
// primitive-topology set {TRIANGLES, LINES, POINTS}. All higher-level
// shapes (boxes, spheres, voxels, normals) are pre-converted upstream by
// GeometryDebug using GeometricPrimitives.
internal static class GltfSerializer
{
    public static void AddShape(SceneBuilder scene, DebugShape shape)
    {
        switch (shape)
        {
            case MeshShape m:   AddMesh(scene, m); break;
            case LinesShape l:  AddLines(scene, l); break;
            case PointsShape p: AddPoints(scene, p); break;
        }
    }

    // SaveGLB writes a single self-contained binary file (JSON + buffers embedded).
    // Atomic-write pattern: write to .tmp, then rename.
    public static void SaveGlb(SceneBuilder scene, string outputPath)
    {
        var tmpPath = outputPath + ".tmp";
        scene.ToGltf2().SaveGLB(tmpPath);
        AtomicFile.MoveWithRetry(tmpPath, outputPath);
    }

    private static void AddMesh(SceneBuilder scene, MeshShape m)
    {
        var nodeName = m.GlobalId is null ? m.Label : $"{m.Label}:{m.GlobalId}";
        var mb       = new MeshBuilder<VertexPosition>(nodeName);
        var prim     = mb.UsePrimitive(MakeMaterial(m.Color, m.Label));

        for (var tid = 0; tid < m.Mesh.MaxTriangleID; tid++)
        {
            if (!m.Mesh.IsTriangle(tid))
            {
                continue;
            }

            var t = m.Mesh.GetTriangle(tid);
            prim.AddTriangle(Vp(m.Mesh.GetVertex(t.a)),
                             Vp(m.Mesh.GetVertex(t.b)),
                             Vp(m.Mesh.GetVertex(t.c)));
        }

        var instance = scene.AddRigidMesh(mb, Matrix4x4.Identity);

        if (m.GlobalId is not null)
        {
            instance.Content.Extras = new JsonObject
            {
                ["globalId"] = m.GlobalId,
                ["ifcType"]  = m.Label,
            };
        }
    }

    private static void AddLines(SceneBuilder scene, LinesShape l)
    {
        var mb   = new MeshBuilder<VertexPosition>(l.Label);
        var prim = mb.UsePrimitive(MakeMaterial(l.Color, l.Label), 2);

        foreach (var (a, b) in l.Segments)
        {
            prim.AddLine(Vp(a), Vp(b));
        }

        scene.AddRigidMesh(mb, Matrix4x4.Identity);
    }

    private static void AddPoints(SceneBuilder scene, PointsShape p)
    {
        var mb   = new MeshBuilder<VertexPosition>(p.Label);
        var prim = mb.UsePrimitive(MakeMaterial(p.Color, p.Label), 1);

        foreach (var pt in p.Points)
        {
            prim.AddPoint(Vp(pt));
        }

        scene.AddRigidMesh(mb, Matrix4x4.Identity);
    }

    private static MaterialBuilder MakeMaterial(Color color, string label)
    {
        var c = color.ToVector4();
        var mat = new MaterialBuilder(label)
            .WithDoubleSide(true)
            .WithUnlitShader()
            .WithBaseColor(c);
        return c.W < 1f ? mat.WithAlpha(AlphaMode.BLEND) : mat;
    }

    private static VertexPosition Vp(Vector3d v) => new((float)v.x, (float)v.y, (float)v.z);
}
