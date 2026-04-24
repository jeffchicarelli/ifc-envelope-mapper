using g4;
using IfcEnvelopeMapper.Core.Domain.Element;
using IfcEnvelopeMapper.Core.Domain.Surface;

namespace IfcEnvelopeMapper.Tests.Core.Surface;

public class FacadeTests
{
    private static BuildingElement MakeElement(string id) => new()
    {
        GlobalId = id,
        IfcType = "IfcWall",
        Mesh = new DMesh3(),
    };

    private static Face MakeFace(BuildingElement element) =>
        new(element, [], new Plane3d(Vector3d.AxisZ, 0), area: 1.0, Vector3d.Zero);

    private static Envelope MakeEnvelope(params Face[] faces) =>
        new(new DMesh3(), faces);

    [Fact]
    public void Constructor_SetsAllProperties()
    {
        var face = MakeFace(MakeElement("e1"));
        var envelope = MakeEnvelope(face);
        var shell = new DMesh3();
        var normal = new Vector3d(0, 1, 0);

        var facade = new Facade("facade-N", envelope, [face], shell, normal, azimuthDegrees: 0);

        facade.Id.Should().Be("facade-N");
        facade.Envelope.Should().BeSameAs(envelope);
        facade.Faces.Should().BeEquivalentTo([face]);
        facade.FacadeShell.Should().BeSameAs(shell);
        facade.DominantNormal.Should().Be(normal);
        facade.AzimuthDegrees.Should().Be(0);
    }

    [Fact]
    public void Elements_ContainsDistinctElementsFromFaces()
    {
        var element = MakeElement("e1");
        var face1 = MakeFace(element);
        var face2 = MakeFace(element);
        var envelope = MakeEnvelope(face1, face2);

        var facade = new Facade("f1", envelope, [face1, face2], new DMesh3(), Vector3d.AxisZ, 0);

        facade.Elements.Should().HaveCount(1);
        facade.Elements[0].Should().BeSameAs(element);
    }

    [Fact]
    public void Elements_ContainsAllElements_WhenAllDistinct()
    {
        var e1 = MakeElement("e1");
        var e2 = MakeElement("e2");
        var face1 = MakeFace(e1);
        var face2 = MakeFace(e2);
        var envelope = MakeEnvelope(face1, face2);

        var facade = new Facade("f1", envelope, [face1, face2], new DMesh3(), Vector3d.AxisZ, 0);

        facade.Elements.Should().HaveCount(2);
    }
}
