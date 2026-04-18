using g4;
using IfcEnvelopeMapper.Core.Detection;
using IfcEnvelopeMapper.Core.Element;
using IfcEnvelopeMapper.Core.Surface;

namespace IfcEnvelopeMapper.Tests.Detection;

public class DetectionResultTests
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
    public void Constructor_SetsEnvelopeAndClassifications()
    {
        var element = MakeElement();
        var face = MakeFace(element);
        var envelope = new Envelope(new DMesh3(), [face]);
        var classification = new ElementClassification(element, isExterior: true, [face]);
        var classifications = new[] { classification };

        var result = new DetectionResult(envelope, classifications);

        result.Envelope.Should().BeSameAs(envelope);
        result.Classifications.Should().BeEquivalentTo(classifications);
    }
}
