using g4;

namespace IfcEnvelopeMapper.Core.Extensions;

public static class Vector3dExtensions
{
    // Best-fit plane via PCA (OrthogonalPlaneFit3 in g4). The plane minimizes
    // the sum of squared perpendicular distances from every point.
    public static Plane3d FitPlane(this IEnumerable<Vector3d> points)
    {
        var list = points as IList<Vector3d> ?? points.ToList();
        var fit = new OrthogonalPlaneFit3(list);
        return new Plane3d(fit.Normal, fit.Origin);
    }

    // UV-sphere centered at this point. rings = latitude bands (poles → equator),
    // sectors = longitude slices. 8x12 is the historical default — cheap enough
    // for hundreds of debug spheres, round enough to read at a glance.
    public static DMesh3 ToSphere(this Vector3d center, double radius, int rings = 8, int sectors = 12)
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
}
