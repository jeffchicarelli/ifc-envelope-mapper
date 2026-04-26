using g4;
using IfcEnvelopeMapper.Core.Domain.Element;
using IfcEnvelopeMapper.Core.Domain.Surface;
using IfcEnvelopeMapper.Core.Pipeline.Bcf;
using IfcEnvelopeMapper.Core.Pipeline.Detection;

namespace IfcEnvelopeMapper.Tests.Core.Pipeline.Bcf;

public sealed class BcfBuilderTests
{
    [Fact]
    public void Build_OneTopicPerExteriorElement()
    {
        // Arrange
        var result = MakeResult(("a", true), ("b", false), ("c", true));

        // Act
        var report = BcfBuilder.Build(result);

        // Assert
        report.Topics.Should().HaveCount(2);
        report.Topics.Select(t => t.Viewpoint.IfcGuid).Should().BeEquivalentTo(new[] { "a", "c" });
    }

    [Fact]
    public void Build_TopicReferencesElementGlobalIdInViewpoint()
    {
        // Arrange
        var result = MakeResult(("xyz-123", true));

        // Act
        var report = BcfBuilder.Build(result);

        // Assert
        report.Topics.Single().Viewpoint.IfcGuid.Should().Be("xyz-123");
    }

    [Fact]
    public void Build_TopicsSortedByIfcGuidOrdinal()
    {
        // Arrange — input deliberately out of order
        var result = MakeResult(("c", true), ("a", true), ("b", true));

        // Act
        var report = BcfBuilder.Build(result);

        // Assert
        report.Topics.Select(t => t.Viewpoint.IfcGuid).Should().Equal("a", "b", "c");
    }

    [Fact]
    public void Build_EmptyInput_ProducesEmptyTopicListAndCorrectVersion()
    {
        // Arrange
        var result = new DetectionResult(
            new Envelope(new DMesh3(), Array.Empty<Face>()),
            Array.Empty<ElementClassification>());

        // Act
        var report = BcfBuilder.Build(result);

        // Assert
        report.Topics.Should().BeEmpty();
        report.Version.Should().Be(BcfBuilder.VERSION);
    }

    [Fact]
    public void Build_Camera_IsUnitDirectionTowardCentroidWithZUp()
    {
        // Arrange — single exterior element centred at origin
        var result = MakeResult(("a", true));

        // Act
        var report = BcfBuilder.Build(result);
        var camera = report.Topics.Single().Viewpoint.Camera;

        // Assert
        camera.Direction.Length.Should().BeApproximately(1.0, 1e-9);
        // Element centred at origin, camera placed at -Y → direction Y should be positive
        camera.Direction.y.Should().BePositive();
        camera.UpVector.Should().Be(new Vector3d(0, 0, 1));
        camera.FieldOfView.Should().Be(60.0);
    }

    private static BuildingElement MakeElement(string id)
    {
        var gen = new TrivialBox3Generator
        {
            Box = new Box3d(Vector3d.Zero, new Vector3d(1.0, 1.0, 1.0))
        };
        return new BuildingElement
        {
            GlobalId = id,
            IfcType  = "IfcWall",
            Mesh     = gen.Generate().MakeDMesh(),
        };
    }

    private static DetectionResult MakeResult(params (string Id, bool IsExterior)[] specs)
    {
        var classifications = specs
            .Select(s => new ElementClassification(MakeElement(s.Id), s.IsExterior, Array.Empty<Face>()))
            .ToList();
        return new DetectionResult(
            new Envelope(new DMesh3(), Array.Empty<Face>()),
            classifications);
    }
}
