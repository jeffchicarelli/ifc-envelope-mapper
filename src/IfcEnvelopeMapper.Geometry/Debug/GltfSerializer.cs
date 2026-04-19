using System.Numerics;
using g4;
using IfcEnvelopeMapper.Geometry.Voxel;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;

namespace IfcEnvelopeMapper.Geometry.Debug;

internal static class GltfSerializer
{
    public static void Flush(List<DebugShape> shapes, string outputPath)
    {
        var scene = new SceneBuilder();
        foreach (var shape in shapes)
        {
            AddShape(scene, shape);
        }

        scene.ToGltf2().SaveGLTF(outputPath);
    }

    private static void AddShape(SceneBuilder scene, DebugShape shape)
    {
        switch (shape)
        {
            case MeshShape m:      AddMesh(scene, m.Mesh, m.Color, m.Label);                                          break;
            case TrianglesShape t: AddTriangles(scene, t.Mesh, t.TriangleIds, t.Color, t.Label);                      break;
            case VoxelsShape v:    AddVoxels(scene, v.Grid, v.Coords, v.Color, v.Label);                              break;
            case PointsShape p:    AddPoints(scene, p.Points, p.Color, p.Label);                                      break;
            case LineShape l:      AddLines(scene, [(l.From, l.To)], l.Color, l.Label);                               break;
            case LinesShape ls:    AddLines(scene, ls.Segments, ls.Color, ls.Label);                                   break;
            case BoxShape b:       AddBox(scene, b.Box, b.Color, b.Label);                                            break;
            case PlaneShape pl:    AddPlane(scene, pl.Plane, pl.DisplaySize, pl.Color, pl.Label);                     break;
            case SphereShape s:    AddSphere(scene, s.Center, s.Radius, s.Color, s.Label);                            break;
            case NormalShape n:    AddLines(scene, [(n.Origin, n.Origin + n.Direction * n.Length)], n.Color, n.Label); break;
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
            .WithUnlitShader()
            .WithBaseColor(c);
        return c.W < 1f ? mat.WithAlpha(AlphaMode.BLEND) : mat;
    }

    // ── Vertex helper ────────────────────────────────────────────────────────

    private static VertexPosition Vp(Vector3d v)              => new((float)v.x, (float)v.y, (float)v.z);
    private static VertexPosition Vp(double x, double y, double z) => new((float)x, (float)y, (float)z);

    // ── Mesh ─────────────────────────────────────────────────────────────────

    private static void AddMesh(SceneBuilder scene, DMesh3 mesh, string color, string label)
    {
        var mb   = new MeshBuilder<VertexPosition>(label);
        var prim = mb.UsePrimitive(MakeMaterial(color, label));

        for (var tid = 0; tid < mesh.TriangleCount; tid++)
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

    private static void AddTriangles(SceneBuilder scene, DMesh3 mesh, int[] ids, string color, string label)
    {
        var mb   = new MeshBuilder<VertexPosition>(label);
        var prim = mb.UsePrimitive(MakeMaterial(color, label));

        foreach (var tid in ids)
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

    // ── Voxels ───────────────────────────────────────────────────────────────

    private static void AddVoxels(SceneBuilder scene, VoxelGrid3D grid,
                                   VoxelCoord[] coords, string color, string label)
    {
        var mb   = new MeshBuilder<VertexPosition>(label);
        var prim = mb.UsePrimitive(MakeMaterial(color, label));

        foreach (var coord in coords)
        {
            AppendCube((a, b, c) => prim.AddTriangle(a, b, c), grid.GetVoxelBox(coord));
        }

        scene.AddRigidMesh(mb, Matrix4x4.Identity);
    }

    // Receives AddTriangle as a delegate — avoids coupling to SharpGLTF generic types.
    private static void AppendCube(Action<VertexPosition, VertexPosition, VertexPosition> tri,
                                    AxisAlignedBox3d box)
    {
        var n = box.Min; var x = box.Max;
        var v0 = Vp(n.x, n.y, n.z); var v1 = Vp(x.x, n.y, n.z);
        var v2 = Vp(x.x, n.y, x.z); var v3 = Vp(n.x, n.y, x.z);
        var v4 = Vp(n.x, x.y, n.z); var v5 = Vp(x.x, x.y, n.z);
        var v6 = Vp(x.x, x.y, x.z); var v7 = Vp(n.x, x.y, x.z);

        tri(v0, v2, v3); tri(v0, v1, v2); // bottom
        tri(v4, v6, v5); tri(v4, v7, v6); // top
        tri(v0, v4, v5); tri(v0, v5, v1); // front
        tri(v2, v6, v7); tri(v2, v7, v3); // back
        tri(v0, v3, v7); tri(v0, v7, v4); // left
        tri(v1, v5, v6); tri(v1, v6, v2); // right
    }

    // ── Lines & Points ───────────────────────────────────────────────────────

    private static void AddLines(SceneBuilder scene,
                                  (Vector3d From, Vector3d To)[] segs, string color, string label)
    {
        var mb   = new MeshBuilder<VertexPosition>(label);
        var prim = mb.UsePrimitive(MakeMaterial(color, label), 1); // 1 = LINES

        foreach (var (a, b) in segs)
        {
            prim.AddLine(Vp(a), Vp(b));
        }

        scene.AddRigidMesh(mb, Matrix4x4.Identity);
    }

    private static void AddPoints(SceneBuilder scene,
                                   Vector3d[] points, string color, string label)
    {
        var mb   = new MeshBuilder<VertexPosition>(label);
        var prim = mb.UsePrimitive(MakeMaterial(color, label), 0); // 0 = POINTS

        foreach (var p in points)
        {
            prim.AddPoint(Vp(p));
        }

        scene.AddRigidMesh(mb, Matrix4x4.Identity);
    }

    // ── Box (wireframe) ──────────────────────────────────────────────────────

    private static void AddBox(SceneBuilder scene, AxisAlignedBox3d box, string color, string label)
    {
        var n = box.Min; var x = box.Max;
        var v0 = new Vector3d(n.x, n.y, n.z); var v1 = new Vector3d(x.x, n.y, n.z);
        var v2 = new Vector3d(x.x, n.y, x.z); var v3 = new Vector3d(n.x, n.y, x.z);
        var v4 = new Vector3d(n.x, x.y, n.z); var v5 = new Vector3d(x.x, x.y, n.z);
        var v6 = new Vector3d(x.x, x.y, x.z); var v7 = new Vector3d(n.x, x.y, x.z);

        (Vector3d, Vector3d)[] edges =
        [
            (v0, v1), (v1, v2), (v2, v3), (v3, v0), // bottom
            (v4, v5), (v5, v6), (v6, v7), (v7, v4), // top
            (v0, v4), (v1, v5), (v2, v6), (v3, v7)  // verticals
        ];
        AddLines(scene, edges, color, label);
    }

    // ── Plane ────────────────────────────────────────────────────────────────

    private static void AddPlane(SceneBuilder scene, Plane3d plane,
                                  double size, string color, string label)
    {
        var normal = plane.Normal.Normalized;
        var up     = Math.Abs(normal.Dot(Vector3d.AxisY)) < 0.99 ? Vector3d.AxisY : Vector3d.AxisX;
        var u      = normal.Cross(up).Normalized * (size * 0.5);
        var v      = normal.Cross(u).Normalized  * (size * 0.5);
        var o      = plane.Normal * plane.Constant; // foot of perpendicular from world origin to plane

        var mb   = new MeshBuilder<VertexPosition>(label);
        var prim = mb.UsePrimitive(MakeMaterial(color, label));
        prim.AddTriangle(Vp(o - u - v), Vp(o + u - v), Vp(o + u + v));
        prim.AddTriangle(Vp(o - u - v), Vp(o + u + v), Vp(o - u + v));
        scene.AddRigidMesh(mb, Matrix4x4.Identity);
    }

    // ── Sphere ───────────────────────────────────────────────────────────────

    private static void AddSphere(SceneBuilder scene, Vector3d center,
                                   double radius, string color, string label)
    {
        const int rings = 8, sectors = 12;
        var verts = new VertexPosition[(rings + 1) * sectors];

        for (var r = 0; r <= rings; r++)
        {
            for (var s = 0; s < sectors; s++)
            {
                var phi   = Math.PI * r / rings;
                var theta = 2 * Math.PI * s / sectors;
                verts[r * sectors + s] = Vp(
                    center.x + radius * Math.Sin(phi) * Math.Cos(theta),
                    center.y + radius * Math.Cos(phi),
                    center.z + radius * Math.Sin(phi) * Math.Sin(theta));
            }
        }

        var mb   = new MeshBuilder<VertexPosition>(label);
        var prim = mb.UsePrimitive(MakeMaterial(color, label));

        for (var r = 0; r < rings;   r++)
        {
            for (var s = 0; s < sectors; s++)
            {
                var cur = r * sectors + s;
                var nxt = r * sectors + (s + 1) % sectors;
                prim.AddTriangle(verts[cur], verts[nxt],           verts[cur + sectors]);
                prim.AddTriangle(verts[nxt], verts[nxt + sectors], verts[cur + sectors]);
            }
        }

        scene.AddRigidMesh(mb, Matrix4x4.Identity);
    }
}
