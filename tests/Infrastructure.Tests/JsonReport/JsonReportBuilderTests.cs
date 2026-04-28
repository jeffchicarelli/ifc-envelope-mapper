using g4;
using IfcEnvelopeMapper.Application.Reports;
using IfcEnvelopeMapper.Domain.Detection;
using IfcEnvelopeMapper.Domain.Surface;

namespace IfcEnvelopeMapper.Infrastructure.Tests.JsonReport;

[Trait("Category", "Integration")]
public sealed class JsonReportBuilderTests : IfcTestBase
{
    private static readonly StrategyConfig _config = new(0.25, null, null, null);

    public JsonReportBuilderTests() : base("duplex.ifc") { }

    [Fact]
    public void Build_ReturnsCurrentSchemaVersion()
    {
        var result = MakeResult(1, 0);

        var report = JsonReportBuilder.Build("foo.ifc", "voxel", _config, result, TimeSpan.Zero);

        report.SchemaVersion.Should().Be(JsonReportBuilder.SCHEMA_VERSION);
    }

    [Fact]
    public void Build_PassesThroughInputAndStrategyAndConfig()
    {
        var result = MakeResult(0, 0);

        var config = new StrategyConfig(null, 16, 7.5, 0.6);

        var report = JsonReportBuilder.Build("models/x.ifc", "raycast", config, result, TimeSpan.FromSeconds(2.5));

        report.Input.Should().Be("models/x.ifc");
        report.Strategy.Should().Be("raycast");
        report.Config.Should().Be(config);

        report.DurationSeconds.Should().BeApproximately(2.5, 1e-9);
    }

    [Fact]
    public void Build_CountsMatchClassifications()
    {
        var result = MakeResult(3, 5);

        var report = JsonReportBuilder.Build("foo.ifc", "voxel", _config, result, TimeSpan.Zero);

        report.ExteriorCount.Should().Be(3);
        report.InteriorCount.Should().Be(5);
        report.Elements.Count.Should().Be(8);
    }

    [Fact]
    public void Build_ElementsAreSortedByGlobalIdOrdinal()
    {
        var result = MakeResult(5, 5);

        var report = JsonReportBuilder.Build("foo.ifc", "voxel", _config, result, TimeSpan.Zero);

        var ids = report.Elements.Select(e => e.GlobalId).ToList();

        var sortedIds = ids.OrderBy(s => s, StringComparer.Ordinal).ToList();

        ids.Should().Equal(sortedIds);
    }

    [Fact]
    public void Build_ElementReport_PreservesGlobalIdAndIfcTypeAndIsExterior()
    {
        var element = Model.Elements[0];

        var result = new DetectionResult(new Envelope(new DMesh3(), Array.Empty<Face>()),
                                         new[] { new ElementClassification(element, true, Array.Empty<Face>()) });

        var report = JsonReportBuilder.Build("foo.ifc", "voxel", _config, result, TimeSpan.Zero);

        var row = report.Elements.Single();
        row.GlobalId.Should().Be(element.GlobalId);
        row.IfcType.Should().Be(element.IfcType);
        row.IsExterior.Should().BeTrue();
    }

    [Fact]
    public void Build_EmptyDetectionResult_ProducesZeroCountsAndEmptyList()
    {
        var result = new DetectionResult(new Envelope(new DMesh3(), Array.Empty<Face>()), Array.Empty<ElementClassification>());

        var report = JsonReportBuilder.Build("foo.ifc", "voxel", _config, result, TimeSpan.Zero);

        report.ExteriorCount.Should().Be(0);
        report.InteriorCount.Should().Be(0);
        report.Elements.Should().BeEmpty();
    }

    [Fact]
    public void Build_GeneratedAt_IsRecentUtcTimestamp()
    {
        var before = DateTimeOffset.UtcNow;

        var report = JsonReportBuilder.Build("foo.ifc", "voxel", _config, MakeResult(0, 0), TimeSpan.Zero);

        var after = DateTimeOffset.UtcNow;

        report.GeneratedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
        report.GeneratedAt.Offset.Should().Be(TimeSpan.Zero, "report must be UTC for determinism across machines");
    }

    private DetectionResult MakeResult(int exteriorCount, int interiorCount)
    {
        var elements = Model.Elements.Take(exteriorCount + interiorCount).ToList();

        var classifications = elements.Select((e, i) => new ElementClassification(e, i < exteriorCount, Array.Empty<Face>())).ToList();

        return new DetectionResult(new Envelope(new DMesh3(), Array.Empty<Face>()), classifications);
    }
}
