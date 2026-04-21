using g4;
using IfcEnvelopeMapper.Geometry.Voxel;

namespace IfcEnvelopeMapper.Geometry.Primitives;

// Pure geometric primitive factories — no debug / no GLB concerns.
// Anything that needs a unit cube, a sphere of N rings, a plane quad, a box
// wireframe, or a voxel-cube set can come here instead of re-implementing.
//
// Returns g4 native types: DMesh3 for surface meshes; segment tuples for
// wireframes. The caller decides what to do with them (render / serialize /
// mesh-process). Primitives themselves know nothing about SharpGLTF or colors.
public static class GeometricPrimitives
{
    // Axis-aligned cube as a closed DMesh3 (12 triangles, 8 vertices).
    // Winding matches the legacy AppendCube in GltfSerializer so shading
    // stays consistent under Lambert flat-shading in the viewer.
    public static DMesh3 Cube(AxisAlignedBox3d box)
    {
        var mesh = new DMesh3();
        var n    = box.Min;
        var x    = box.Max;

        var v0 = mesh.AppendVertex(new Vector3d(n.x, n.y, n.z));
        var v1 = mesh.AppendVertex(new Vector3d(x.x, n.y, n.z));
        var v2 = mesh.AppendVertex(new Vector3d(x.x, n.y, x.z));
        var v3 = mesh.AppendVertex(new Vector3d(n.x, n.y, x.z));
        var v4 = mesh.AppendVertex(new Vector3d(n.x, x.y, n.z));
        var v5 = mesh.AppendVertex(new Vector3d(x.x, x.y, n.z));
        var v6 = mesh.AppendVertex(new Vector3d(x.x, x.y, x.z));
        var v7 = mesh.AppendVertex(new Vector3d(n.x, x.y, x.z));

        mesh.AppendTriangle(new Index3i(v0, v2, v3)); mesh.AppendTriangle(new Index3i(v0, v1, v2)); // bottom
        mesh.AppendTriangle(new Index3i(v4, v6, v5)); mesh.AppendTriangle(new Index3i(v4, v7, v6)); // top
        mesh.AppendTriangle(new Index3i(v0, v4, v5)); mesh.AppendTriangle(new Index3i(v0, v5, v1)); // front
        mesh.AppendTriangle(new Index3i(v2, v6, v7)); mesh.AppendTriangle(new Index3i(v2, v7, v3)); // back
        mesh.AppendTriangle(new Index3i(v0, v3, v7)); mesh.AppendTriangle(new Index3i(v0, v7, v4)); // left
        mesh.AppendTriangle(new Index3i(v1, v5, v6)); mesh.AppendTriangle(new Index3i(v1, v6, v2)); // right
        return mesh;
    }

    // UV-sphere as a DMesh3. rings = latitude bands (poles → equator),
    // sectors = longitude slices. 8×12 is the historical default — cheap
    // enough for hundreds of debug spheres, round enough to read at a glance.
    public static DMesh3 Sphere(Vector3d center, double radius, int rings = 8, int sectors = 12)
    {
        var mesh    = new DMesh3();
        var indices = new int[(rings + 1) * sectors];

        for (var r = 0; r <= rings; r++)
        {
            for (var s = 0; s < sectors; s++)
            {
                var phi   = Math.PI * r / rings;
                var theta = 2 * Math.PI * s / sectors;
                indices[r * sectors + s] = mesh.AppendVertex(new Vector3d(
                    center.x + radius * Math.Sin(phi) * Math.Cos(theta),
                    center.y + radius * Math.Cos(phi),
                    center.z + radius * Math.Sin(phi) * Math.Sin(theta)));
            }
        }

        for (var r = 0; r < rings; r++)
        {
            for (var s = 0; s < sectors; s++)
            {
                var cur = r * sectors + s;
                var nxt = r * sectors + (s + 1) % sectors;
                mesh.AppendTriangle(new Index3i(indices[cur], indices[nxt],           indices[cur + sectors]));
                mesh.AppendTriangle(new Index3i(indices[nxt], indices[nxt + sectors], indices[cur + sectors]));
            }
        }
        return mesh;
    }

    // Finite square quad sampled from an infinite plane (two triangles).
    // `displaySize` is the edge length; quad is centered at the plane's foot
    // of perpendicular from world origin. Useful for visualizing fit planes.
    public static DMesh3 PlaneQuad(Plane3d plane, double displaySize)
    {
        var normal = plane.Normal.Normalized;
        // Pick any world axis not nearly parallel to the normal as a seed for the frame.
        var up = Math.Abs(normal.Dot(Vector3d.AxisY)) < 0.99 ? Vector3d.AxisY : Vector3d.AxisX;
        var u  = normal.Cross(up).Normalized * (displaySize * 0.5);
        var v  = normal.Cross(u).Normalized  * (displaySize * 0.5);
        var o  = plane.Normal * plane.Constant;

        var mesh = new DMesh3();
        var a    = mesh.AppendVertex(o - u - v);
        var b    = mesh.AppendVertex(o + u - v);
        var c    = mesh.AppendVertex(o + u + v);
        var d    = mesh.AppendVertex(o - u + v);
        mesh.AppendTriangle(new Index3i(a, b, c));
        mesh.AppendTriangle(new Index3i(a, c, d));
        return mesh;
    }

    // 12 edges of an axis-aligned box as From→To segments.
    // Wireframe is lines, not triangles, so DMesh3 isn't the right payload.
    public static IEnumerable<(Vector3d From, Vector3d To)> BoxWireframe(AxisAlignedBox3d box)
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

    // One DMesh3 containing a cube per voxel coord, optionally shrunk to help
    // the eye parse neighboring voxels. `shrinkFactor` ∈ (0,1] — 1.0 is
    // cells-touching, 0.25 (the default debug policy) leaves a gap 3× wider
    // than the voxel itself. Batching into a single mesh keeps the viewer
    // scene graph flat (one layer per call, not one per voxel).
    public static DMesh3 VoxelCubes(VoxelGrid3D grid, IEnumerable<VoxelCoord> coords, double shrinkFactor = 1.0)
    {
        var mesh = new DMesh3();
        foreach (var coord in coords)
        {
            var box = grid.GetVoxelBox(coord);
            AppendCube(mesh, Shrink(box, shrinkFactor));
        }
        return mesh;
    }

    private static AxisAlignedBox3d Shrink(AxisAlignedBox3d box, double factor)
    {
        if (Math.Abs(factor - 1.0) < 1e-9)
        {
            return box;
        }

        var center = box.Center;
        var half   = new Vector3d(box.Width * 0.5, box.Height * 0.5, box.Depth * 0.5) * factor;
        return new AxisAlignedBox3d(center - half, center + half);
    }

    // Internal helper — the batched version of Cube that appends into an
    // existing mesh (avoids allocating N separate meshes for N voxels).
    private static void AppendCube(DMesh3 mesh, AxisAlignedBox3d box)
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
