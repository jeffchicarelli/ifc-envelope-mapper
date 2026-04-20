using g4;
using IfcEnvelopeMapper.Core.Element;

namespace IfcEnvelopeMapper.Geometry.Operations;

public static class GeometricOperations
{
    public static AxisAlignedBox3d BoundingBox(List<BuildingElement> elements)
    {
        var bbox = elements[0].Mesh.GetBounds();
        for (var i = 1; i < elements.Count; i++)
        {
            bbox.Contain(elements[i].Mesh.GetBounds());
        }

        return bbox;
    }

    public static Plane3d FitPlane(List<Vector3d> points)
    {
        var fit = new OrthogonalPlaneFit3(points);
        return new Plane3d(fit.Normal, fit.Origin);
    }

    public static Vector3d TriangleNormal(Vector3d a, Vector3d b, Vector3d c)
    {
        return (b - a).Cross(c - a).Normalized;
    }
}
