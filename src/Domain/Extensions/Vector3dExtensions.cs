using g4;

namespace IfcEnvelopeMapper.Domain.Extensions;

/// <summary>Extension methods on <see cref="g4.Vector3d"/>.</summary>
public static class Vector3dExtensions
{
    /// <summary>
    /// Best-fit plane through the given points via PCA.
    /// Minimises the sum of squared perpendicular distances from every point.
    /// </summary>
    public static Plane3d FitPlane(this IEnumerable<Vector3d> points)
    {
        var list = points as IList<Vector3d> ?? points.ToList();
        var fit = new OrthogonalPlaneFit3(list);
        return new Plane3d(fit.Normal, fit.Origin);
    }

    /// <summary>
    /// UV-sphere mesh centred at this point.
    /// </summary>
    /// <param name="center">Center point of the sphere.</param>
    /// <param name="radius">Sphere radius.</param>
    /// <param name="rings">Latitude bands (pole to pole). Default 8 — cheap enough for many debug spheres, round enough to read at a glance.</param>
    /// <param name="sectors">Longitude slices. Default 12.</param>
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
