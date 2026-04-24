using g4;

namespace IfcEnvelopeMapper.Core.Extensions;

public static class Plane3dExtensions
{
    // Finite square quad sampled from this (infinite) plane. `displaySize` is
    // the edge length; quad is centered at the plane's foot of perpendicular
    // from world origin. Useful for visualizing fit planes.
    public static DMesh3 ToQuadMesh(this Plane3d plane, double displaySize)
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
}
