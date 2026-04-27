using g4;
using IfcEnvelopeMapper.Engine.Pipeline.Detection;
using IfcEnvelopeMapper.Engine.Pipeline.JsonReport;
using IfcEnvelopeMapper.Ifc.Domain.Surface;

namespace IfcEnvelopeMapper.Tests.Engine.Pipeline.JsonReport;

/// <summary>
/// Unit tests for <see cref="ReportBuilder.Build"/>. Builds synthetic
/// <see cref="DetectionResult"/>s using real <c>Element</c>s from
/// <c>duplex.ifc</c> (the only way to get an <c>Element</c> outside the
/// loader) and verifies the mapping to <see cref="DetectionReport"/>.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ReportBuilderTests : IfcTestBase
{
    private static readonly StrategyConfig CONFIG =
        new(VoxelSize: 0.25, NumRays: null, JitterDeg: null, HitRatio: null);

    public ReportBuilderTests() : base("duplex.ifc") { }

    [Fact]
    public void Build_ReturnsCurrentSchemaVersion()
    {
        var result = MakeResult(exteriorCount: 1, interiorCount: 0);

        var report = ReportBuilder.Build("foo.ifc", "voxel", CONFIG, result, TimeSpan.Zero);

        report.SchemaVersion.Should().Be(ReportBuilder.SCHEMA_VERSION);
    }

    [Fact]
    public void Build_PassesThroughInputAndStrategyAndConfig()
    {
        var result = MakeResult(exteriorCount: 0, interiorCount: 0);
        var config = new StrategyConfig(VoxelSize: null, NumRays: 16, JitterDeg: 7.5, HitRatio: 0.6);

        var report = ReportBuilder.Build("models/x.ifc", "raycast", config, result, TimeSpan.FromSeconds(2.5));

        report.Input.Should().Be("models/x.ifc");
        report.Strategy.Should().Be("raycast");
        report.Config.Should().Be(config);
        report.DurationSeconds.Should().BeApproximately(2.5, 1e-9);
    }

    [Fact]
    public void Build_CountsMatchClassifications()
    {
        var result = MakeResult(exteriorCount: 3, interiorCount: 5);

        var report = ReportBuilder.Build("foo.ifc", "voxel", CONFIG, result, TimeSpan.Zero);

        report.ExteriorCount.Should().Be(3);
        report.InteriorCount.Should().Be(5);
        report.Elements.Count.Should().Be(8);
    }

    [Fact]
    public void Build_ElementsAreSortedByGlobalIdOrdinal()
    {
        // Determinism contract: two runs over the same input must produce
        // byte-identical JSON. ReportBuilder enforces ordinal sort.
        var result = MakeResult(exteriorCount: 5, interiorCount: 5);

        var report = ReportBuilder.Build("foo.ifc", "voxel", CONFIG, result, TimeSpan.Zero);

        var ids       = report.Elements.Select(e => e.GlobalId).ToList();
        var sortedIds = ids.OrderBy(s => s, StringComparer.Ordinal).ToList();
        ids.Should().Equal(sortedIds);
    }

    [Fact]
    public void Build_ElementReport_PreservesGlobalIdAndIfcTypeAndIsExterior()
    {
        var element = Model.Elements[0];
        var result  = new DetectionResult(
            envelope:        new Envelope(new DMesh3(), Array.Empty<Face>()),
            classifications: new[] { new ElementClassification(element, isExterior: true, Array.Empty<Face>()) });

        var report = ReportBuilder.Build("foo.ifc", "voxel", CONFIG, result, TimeSpan.Zero);

        var row = report.Elements.Single();
        row.GlobalId.Should().Be(element.GlobalId);
        row.IfcType.Should().Be(element.IfcType);
        row.IsExterior.Should().BeTrue();
    }

    [Fact]
    public void Build_EmptyDetectionResult_ProducesZeroCountsAndEmptyList()
    {
        var result = new DetectionResult(
            envelope:        new Envelope(new DMesh3(), Array.Empty<Face>()),
            classifications: Array.Empty<ElementClassification>());

        var report = ReportBuilder.Build("foo.ifc", "voxel", CONFIG, result, TimeSpan.Zero);

        report.ExteriorCount.Should().Be(0);
        report.InteriorCount.Should().Be(0);
        report.Elements.Should().BeEmpty();
    }

    [Fact]
    public void Build_GeneratedAt_IsRecentUtcTimestamp()
    {
        var before = DateTimeOffset.UtcNow;
        var report = ReportBuilder.Build("foo.ifc", "voxel", CONFIG, MakeResult(0, 0), TimeSpan.Zero);
        var after  = DateTimeOffset.UtcNow;

        report.GeneratedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
        report.GeneratedAt.Offset.Should().Be(TimeSpan.Zero, "report must be UTC for determinism across machines");
    }

    private DetectionResult MakeResult(int exteriorCount, int interiorCount)
    {
        var elements = Model.Elements.Take(exteriorCount + interiorCount).ToList();
        var classifications = elements
            .Select((e, i) => new ElementClassification(e, isExterior: i < exteriorCount, Array.Empty<Face>()))
            .ToList();

        return new DetectionResult(
            envelope:        new Envelope(new DMesh3(), Array.Empty<Face>()),
            classifications: classifications);
    }
}
