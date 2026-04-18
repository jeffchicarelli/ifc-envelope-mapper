using g4;
using IfcEnvelopeMapper.Core.Element;

namespace IfcEnvelopeMapper.Core.Surface;

public sealed class Face
{
    public BuildingElement Element { get; }
    public IReadOnlyList<int> TriangleIds { get; }
    public Plane3d FittedPlane { get; }
    public Vector3d Normal => FittedPlane.Normal;
    public double Area { get; }
    public Vector3d Centroid { get; }

    public Face(
        BuildingElement element,
        IReadOnlyList<int> triangleIds,
        Plane3d fittedPlane,
        double area,
        Vector3d centroid)
    {
        Element = element;
        TriangleIds = triangleIds;
        FittedPlane = fittedPlane;
        Area = area;
        Centroid = centroid;
    }
}
