using g4;

namespace IfcEnvelopeMapper.Core.Extensions;

public static class AxisAlignedBox3dExtensions
{
    // Closed cube mesh from this box (12 triangles, 8 vertices). Winding
    // matches the legacy AppendCube in GltfSerializer so shading stays
    // consistent under Lambert flat-shading in the viewer.
    public static DMesh3 ToCube(this AxisAlignedBox3d box)
    {
        var mesh = new DMesh3();
        AppendCube(mesh, box);
        return mesh;
    }

    // 12 edges of the box as From→To segments. Wireframe is lines, not
    // triangles, so DMesh3 isn't the right payload.
    public static IEnumerable<(Vector3d From, Vector3d To)> ToWireframe(this AxisAlignedBox3d box)
    {
        var n = box.Min;
        var x = box.Max;
        var v0 = new Vector3d(n.x, n.y, n.z); var v1 = new Vector3d(x.x, n.y, n.z);
        var v2 = new Vector3d(x.x, n.y, x.z); var v3 = new Vector3d(n.x, n.y, x.z);
        var v4 = new Vector3d(n.x, x.y, n.z); var v5 = new Vector3d(x.x, x.y, n.z);
        var v6 = new Vector3d(x.x, x.y, x.z); var v7 = new Vector3d(n.x, x.y, x.z);

        yield return (v0, v1); yield return (v1, v2); yield return (v2, v3); yield return (v3, v0); // bottom
        yield return (v4, v5); yield return (v5, v6); yield return (v6, v7); yield return (v7, v4); // top
        yield return (v0, v4); yield return (v1, v5); yield return (v2, v6); yield return (v3, v7); // verticals
    }

    // Internal helper — used by ToCube here and by VoxelGrid3DExtensions.CubesAt
    // (batched cube emission per voxel coord).
    internal static void AppendCube(DMesh3 mesh, AxisAlignedBox3d box)
    {
        var n = box.Min;
        var x = box.Max;

        var v0 = mesh.AppendVertex(new Vector3d(n.x, n.y, n.z));
        var v1 = mesh.AppendVertex(new Vector3d(x.x, n.y, n.z));
        var v2 = mesh.AppendVertex(new Vector3d(x.x, n.y, x.z));
        var v3 = mesh.AppendVertex(new Vector3d(n.x, n.y, x.z));
        var v4 = mesh.AppendVertex(new Vector3d(n.x, x.y, n.z));
        var v5 = mesh.AppendVertex(new Vector3d(x.x, x.y, n.z));
        var v6 = mesh.AppendVertex(new Vector3d(x.x, x.y, x.z));
        var v7 = mesh.AppendVertex(new Vector3d(n.x, x.y, x.z));

        mesh.AppendTriangle(new Index3i(v0, v2, v3)); mesh.AppendTriangle(new Index3i(v0, v1, v2));
        mesh.AppendTriangle(new Index3i(v4, v6, v5)); mesh.AppendTriangle(new Index3i(v4, v7, v6));
        mesh.AppendTriangle(new Index3i(v0, v4, v5)); mesh.AppendTriangle(new Index3i(v0, v5, v1));
        mesh.AppendTriangle(new Index3i(v2, v6, v7)); mesh.AppendTriangle(new Index3i(v2, v7, v3));
        mesh.AppendTriangle(new Index3i(v0, v3, v7)); mesh.AppendTriangle(new Index3i(v0, v7, v4));
        mesh.AppendTriangle(new Index3i(v1, v5, v6)); mesh.AppendTriangle(new Index3i(v1, v6, v2));
    }
}
