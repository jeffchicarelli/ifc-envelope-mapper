using g4;
using IfcEnvelopeMapper.Core.Domain.Element;
using IfcEnvelopeMapper.Core.Domain.Surface;
using IfcEnvelopeMapper.Core.Pipeline.Detection;
using IfcEnvelopeMapper.Core.Pipeline.Reporting;

namespace IfcEnvelopeMapper.Tests.Core.Pipeline.Reporting;

public sealed class ReportBuilderTests
{
    [Fact]
    public void Build_PopulatesScalarFields()
    {
        // Arrange
        var result = MakeResult(
            ("a", "IfcWall",  true),
            ("b", "IfcSlab",  false),
            ("c", "IfcMember", true));
        var config = new StrategyConfig(VoxelSize: 0.25, NumRays: null, JitterDeg: null, HitRatio: null);

        // Act
        var report = ReportBuilder.Build(
            ifcPath: "/x/duplex.ifc",
            strategy: "voxel",
            config: config,
            result: result,
            duration: TimeSpan.FromMilliseconds(1234));

        // Assert
        report.SchemaVersion.Should().Be(ReportBuilder.SCHEMA_VERSION);
        report.Input.Should().Be("/x/duplex.ifc");
        report.Strategy.Should().Be("voxel");
        report.Config.Should().Be(config);
        report.ExteriorCount.Should().Be(2);
        report.InteriorCount.Should().Be(1);
        report.DurationSeconds.Should().BeApproximately(1.234, 1e-9);
        report.GeneratedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Build_ElementsSortedByGlobalIdOrdinal()
    {
        // Arrange — input deliberately out of order
        var result = MakeResult(
            ("c", "IfcWall", true),
            ("a", "IfcSlab", false),
            ("b", "IfcMember", true));

        // Act
        var report = ReportBuilder.Build(
            "x.ifc", "voxel",
            new StrategyConfig(0.25, null, null, null),
            result, TimeSpan.Zero);

        // Assert
        report.Elements.Select(e => e.GlobalId).Should().Equal("a", "b", "c");
    }

    [Fact]
    public void Build_PreservesElementClassification()
    {
        // Arrange
        var result = MakeResult(
            ("a", "IfcWall", true),
            ("b", "IfcSlab", false));

        // Act
        var report = ReportBuilder.Build(
            "x.ifc", "voxel",
            new StrategyConfig(0.25, null, null, null),
            result, TimeSpan.Zero);

        // Assert
        report.Elements.Single(e => e.GlobalId == "a").IsExterior.Should().BeTrue();
        report.Elements.Single(e => e.GlobalId == "b").IsExterior.Should().BeFalse();
        report.Elements.Single(e => e.GlobalId == "a").IfcType.Should().Be("IfcWall");
        report.Elements.Single(e => e.GlobalId == "b").IfcType.Should().Be("IfcSlab");
    }

    [Fact]
    public void Build_EmptyResult_ProducesEmptyElementsAndZeroCounts()
    {
        // Arrange
        var result = new DetectionResult(
            new Envelope(new DMesh3(), Array.Empty<Face>()),
            Array.Empty<ElementClassification>());

        // Act
        var report = ReportBuilder.Build(
            "x.ifc", "voxel",
            new StrategyConfig(0.25, null, null, null),
            result, TimeSpan.Zero);

        // Assert
        report.Elements.Should().BeEmpty();
        report.ExteriorCount.Should().Be(0);
        report.InteriorCount.Should().Be(0);
    }

    private static DetectionResult MakeResult(params (string Id, string IfcType, bool IsExterior)[] elements)
    {
        var classifications = elements
            .Select(e => new ElementClassification(
                new BuildingElement
                {
                    GlobalId = e.Id,
                    IfcType  = e.IfcType,
                    Mesh     = new DMesh3(),
                },
                e.IsExterior,
                Array.Empty<Face>()))
            .ToList();

        return new DetectionResult(
            new Envelope(new DMesh3(), Array.Empty<Face>()),
            classifications);
    }
}
