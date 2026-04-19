using g4;
using IfcEnvelopeMapper.Core.Element;
using IfcEnvelopeMapper.Core.Surface;

namespace IfcEnvelopeMapper.Tests.Core.Surface;

public class FaceTests
{
    private static BuildingElement MakeElement() => new()
    {
        GlobalId = "elem-1",
        IfcType = "IfcWall",
        Mesh = new DMesh3(),
    };

    [Fact]
    public void Constructor_SetsAllProperties()
    {
        var element = MakeElement();
        int[] triangleIds = [0, 1, 2];
        var plane = new Plane3d(new Vector3d(0, 0, 1), 0.0);
        var centroid = new Vector3d(1, 2, 3);

        var face = new Face(element, triangleIds, plane, area: 10.5, centroid);

        face.Element.Should().BeSameAs(element);
        face.TriangleIds.Should().BeEquivalentTo(triangleIds);
        face.FittedPlane.Should().Be(plane);
        face.Area.Should().Be(10.5);
        face.Centroid.Should().Be(centroid);
    }

    [Fact]
    public void Normal_ReturnsFittedPlaneNormal()
    {
        var normal = new Vector3d(0, 0, 1);
        var plane = new Plane3d(normal, 0.0);
        var face = new Face(MakeElement(), [0], plane, area: 1.0, Vector3d.Zero);

        face.Normal.Should().Be(normal);
    }
}
