using System.Numerics;
using System.Text.Json.Nodes;
using g4;
using IfcEnvelopeMapper.Infrastructure.Visualization.Api;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;

namespace IfcEnvelopeMapper.Infrastructure.Visualization.Serialization;

/// <summary>
/// Encodes <see cref="DebugShape"/> payloads as glTF primitives attached to a long-lived <c>SceneBuilder</c>.
/// Each shape becomes its own glTF node — no per-(label, color) merging — allowing incremental accumulation
/// across flushes. Per-shape <c>AddTriangle</c> iteration happens once on first emission; <see cref="SaveGlb"/>
/// is the only per-flush cost. Three payload kinds — Mesh / Lines / Points — match the glTF
/// primitive-topology set {TRIANGLES, LINES, POINTS}; higher-level shapes are pre-converted by
/// <see cref="GeometryDebug"/>. Consumed only by <see cref="Scene"/>.
/// </summary>
internal static class GltfSerializer
{
    public static void AddShape(SceneBuilder scene, DebugShape shape)
    {
        switch (shape)
        {
            case MeshShape m:
                AddMesh(scene, m);
                break;
            case LinesShape l:
                AddLines(scene, l);
                break;
            case PointsShape p:
                AddPoints(scene, p);
                break;
        }
    }

    /// <summary>
    /// Serialises <paramref name="scene"/> to a self-contained GLB at <paramref name="outputPath"/>.
    /// Uses the atomic-write pattern (write to <c>.tmp</c>, then rename) to avoid mid-flight reads.
    /// </summary>
    public static void SaveGlb(SceneBuilder scene, string outputPath)
    {
        var tmpPath = outputPath + ".tmp";
        scene.ToGltf2().SaveGLB(tmpPath);

        AtomicFile.MoveWithRetry(tmpPath, outputPath);
    }

    private static void AddMesh(SceneBuilder scene, MeshShape m)
    {
        var nodeName = m.GlobalId is null ? m.Label : $"{m.Label}:{m.GlobalId}";
        var mb = new MeshBuilder<VertexPosition>(nodeName);

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

        if (m.GlobalId is not null)
        {
            instance.Content.Extras = new JsonObject { ["globalId"] = m.GlobalId, ["ifcType"] = m.Label };
        }
    }

    private static void AddLines(SceneBuilder scene, LinesShape l)
    {
        var mb = new MeshBuilder<VertexPosition>(l.Label);

        var prim = mb.UsePrimitive(MakeMaterial(l.Color, l.Label), 2);

        foreach (var (a, b) in l.Segments)
        {
            prim.AddLine(Vp(a), Vp(b));
        }

        scene.AddRigidMesh(mb, Matrix4x4.Identity);
    }

    private static void AddPoints(SceneBuilder scene, PointsShape p)
    {
        var mb = new MeshBuilder<VertexPosition>(p.Label);

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

        var mat = new MaterialBuilder(label).WithDoubleSide(true).WithUnlitShader().WithBaseColor(c);

        return c.W < 1f ? mat.WithAlpha(AlphaMode.BLEND) : mat;
    }

    private static VertexPosition Vp(Vector3d v)
    {
        return new VertexPosition((float)v.x, (float)v.y, (float)v.z);
    }
}
