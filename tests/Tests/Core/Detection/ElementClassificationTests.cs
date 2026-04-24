using g4;
using IfcEnvelopeMapper.Core.Pipeline.Detection;
using IfcEnvelopeMapper.Core.Domain.Element;
using IfcEnvelopeMapper.Core.Domain.Surface;

namespace IfcEnvelopeMapper.Tests.Core.Detection;

public class ElementClassificationTests
{
    private static BuildingElement MakeElement() => new()
    {
        GlobalId = "e1",
        IfcType = "IfcWall",
        Mesh = new DMesh3(),
    };

    private static Face MakeFace(BuildingElement element) =>
        new(element, [], new Plane3d(Vector3d.AxisZ, 0), area: 1.0, Vector3d.Zero);

    [Fact]
    public void Constructor_SetsAllProperties()
    {
        var element = MakeElement();
        var face = MakeFace(element);
        var externalFaces = new[] { face };

        var classification = new ElementClassification(element, isExterior: true, externalFaces);

        classification.Element.Should().BeSameAs(element);
        classification.IsExterior.Should().BeTrue();
        classification.ExternalFaces.Should().BeEquivalentTo(externalFaces);
    }

    [Fact]
    public void Interior_Element_HasEmptyExternalFaces()
    {
        var element = MakeElement();

        var classification = new ElementClassification(element, isExterior: false, []);

        classification.IsExterior.Should().BeFalse();
        classification.ExternalFaces.Should().BeEmpty();
    }
}
