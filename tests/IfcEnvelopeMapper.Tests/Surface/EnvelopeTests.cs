using g4;
using IfcEnvelopeMapper.Core.Element;
using IfcEnvelopeMapper.Core.Surface;

namespace IfcEnvelopeMapper.Tests.Surface;

public class EnvelopeTests
{
    private static BuildingElement MakeElement(string id) => new()
    {
        GlobalId = id,
        IfcType = "IfcWall",
        Mesh = new DMesh3(),
    };

    private static Face MakeFace(BuildingElement element) =>
        new(element, [], new Plane3d(Vector3d.AxisZ, 0), area: 1.0, Vector3d.Zero);

    [Fact]
    public void Constructor_SetsShellAndFaces()
    {
        var shell = new DMesh3();
        var face = MakeFace(MakeElement("e1"));
        var faces = new[] { face };

        var envelope = new Envelope(shell, faces);

        envelope.Shell.Should().BeSameAs(shell);
        envelope.Faces.Should().BeEquivalentTo(faces);
    }

    [Fact]
    public void Elements_ContainsDistinctElementsFromFaces()
    {
        var element = MakeElement("e1");
        var face1 = MakeFace(element);
        var face2 = MakeFace(element);

        var envelope = new Envelope(new DMesh3(), [face1, face2]);

        envelope.Elements.Should().HaveCount(1);
        envelope.Elements[0].Should().BeSameAs(element);
    }

    [Fact]
    public void Elements_ContainsAllElements_WhenAllDistinct()
    {
        var e1 = MakeElement("e1");
        var e2 = MakeElement("e2");
        var face1 = MakeFace(e1);
        var face2 = MakeFace(e2);

        var envelope = new Envelope(new DMesh3(), [face1, face2]);

        envelope.Elements.Should().HaveCount(2);
    }
}
