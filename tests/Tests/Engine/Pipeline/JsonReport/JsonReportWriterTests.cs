using System.Text.Json;
using IfcEnvelopeMapper.Engine.Pipeline.Detection;
using IfcEnvelopeMapper.Engine.Pipeline.JsonReport;

namespace IfcEnvelopeMapper.Tests.Engine.Pipeline.JsonReport;

/// <summary>
/// Unit tests for <see cref="JsonReportWriter.Write"/>. The writer is a thin
/// wrapper around <c>System.Text.Json</c>; tests cover the contract pieces
/// that matter to consumers of the report file:
/// <list type="bullet">
/// <item><description>camelCase property names (matches the JS-side viewer).</description></item>
/// <item><description>Indented output (human-readable diffs).</description></item>
/// <item><description>Round-trips back to a structurally equal record.</description></item>
/// <item><description>Auto-creates a missing parent directory.</description></item>
/// </list>
/// Element-bearing reports go through <see cref="ReportBuilder"/> and are
/// covered by the integration tests; these unit tests build
/// <see cref="DetectionReport"/> directly so no IFC fixture is needed.
/// </summary>
public sealed class JsonReportWriterTests : IDisposable
{
    private readonly List<string> _tempPaths = new();

    [Fact]
    public void Write_RoundTrips_WithCamelCasePropertyNames()
    {
        var report = MakeReport();
        var path   = NewTempPath();

        JsonReportWriter.Write(report, path);

        var json = File.ReadAllText(path);
        json.Should().Contain("\"schemaVersion\":");
        json.Should().Contain("\"elements\":");
        json.Should().NotContain("\"SchemaVersion\":", "expected camelCase, not PascalCase");
        json.Should().NotContain("\"Elements\":");

        var roundtripped = JsonSerializer.Deserialize<DetectionReport>(
            json,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        roundtripped.Should().NotBeNull();
        roundtripped!.SchemaVersion.Should().Be(report.SchemaVersion);
        roundtripped.Input.Should().Be(report.Input);
        roundtripped.Strategy.Should().Be(report.Strategy);
        roundtripped.ExteriorCount.Should().Be(report.ExteriorCount);
        roundtripped.InteriorCount.Should().Be(report.InteriorCount);
        roundtripped.Elements.Should().HaveCount(report.Elements.Count);
        roundtripped.Elements[0].GlobalId.Should().Be(report.Elements[0].GlobalId);
    }

    [Fact]
    public void Write_OutputIsIndented_NotMinified()
    {
        var report = MakeReport();
        var path   = NewTempPath();

        JsonReportWriter.Write(report, path);

        var json = File.ReadAllText(path);
        json.Should().Contain("\n", "WriteIndented should produce multi-line JSON");
        json.Split('\n').Length.Should().BeGreaterThan(5);
    }

    [Fact]
    public void Write_OverwritesExistingFile()
    {
        var path = NewTempPath();
        File.WriteAllText(path, "stale-content");

        JsonReportWriter.Write(MakeReport(), path);

        File.ReadAllText(path).Should().NotContain("stale-content");
        File.ReadAllText(path).Should().Contain("schemaVersion");
    }

    [Fact]
    public void Write_CreatesParentDirectoryIfMissing()
    {
        // Common case: caller passes a path under data/results/<run>/ without
        // having created the run-specific subfolder. The writer must MkDir-p
        // the parent rather than throwing.
        var dir  = Path.Combine(Path.GetTempPath(), $"json-report-test-{Guid.NewGuid():N}", "nested", "level");
        var path = Path.Combine(dir, "report.json");
        _tempPaths.Add(path);

        Directory.Exists(dir).Should().BeFalse();

        JsonReportWriter.Write(MakeReport(), path);

        File.Exists(path).Should().BeTrue();

        // Cleanup the parent tree
        try { Directory.Delete(Path.GetDirectoryName(Path.GetDirectoryName(dir)!)!, recursive: true); }
        catch { /* best-effort */ }
    }

    [Fact]
    public void Write_EmptyElementList_StillProducesValidJson()
    {
        var report = new DetectionReport(
            SchemaVersion:   "1",
            Input:           "test.ifc",
            Strategy:        "voxel",
            Config:          new StrategyConfig(VoxelSize: 0.25, NumRays: null, JitterDeg: null, HitRatio: null),
            ExteriorCount:   0,
            InteriorCount:   0,
            Elements:        Array.Empty<ElementReport>(),
            GeneratedAt:     DateTimeOffset.UtcNow,
            DurationSeconds: 0.0);
        var path = NewTempPath();

        JsonReportWriter.Write(report, path);

        var json = File.ReadAllText(path);
        json.Should().Contain("\"elements\": []");
    }

    [Fact]
    public void Write_StrategyConfig_NullableFields_SerializeAsNull()
    {
        // RayCasting populates NumRays/JitterDeg/HitRatio and leaves VoxelSize null;
        // Voxel does the inverse. The JSON has to faithfully carry the nulls
        // so downstream consumers can tell which strategy ran.
        var voxelReport = new DetectionReport(
            SchemaVersion:   "1",
            Input:           "test.ifc",
            Strategy:        "voxel",
            Config:          new StrategyConfig(VoxelSize: 0.5, NumRays: null, JitterDeg: null, HitRatio: null),
            ExteriorCount:   0,
            InteriorCount:   0,
            Elements:        Array.Empty<ElementReport>(),
            GeneratedAt:     DateTimeOffset.UtcNow,
            DurationSeconds: 0.0);
        var path = NewTempPath();

        JsonReportWriter.Write(voxelReport, path);

        var json = File.ReadAllText(path);
        json.Should().Contain("\"voxelSize\": 0.5");
        json.Should().Contain("\"numRays\": null");
        json.Should().Contain("\"jitterDeg\": null");
        json.Should().Contain("\"hitRatio\": null");
    }

    private static DetectionReport MakeReport() => new(
        SchemaVersion:   "1",
        Input:           "fixture.ifc",
        Strategy:        "voxel",
        Config:          new StrategyConfig(VoxelSize: 0.25, NumRays: null, JitterDeg: null, HitRatio: null),
        ExteriorCount:   2,
        InteriorCount:   1,
        Elements:        new[]
        {
            new ElementReport(GlobalId: "01HVKY",  IfcType: "IfcWall",   IsExterior: true),
            new ElementReport(GlobalId: "02ABCD",  IfcType: "IfcWindow", IsExterior: true),
            new ElementReport(GlobalId: "03ZZZZ",  IfcType: "IfcSlab",   IsExterior: false),
        },
        GeneratedAt:     DateTimeOffset.Parse("2026-04-27T10:00:00Z"),
        DurationSeconds: 1.234);

    private string NewTempPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"json-report-test-{Guid.NewGuid():N}.json");
        _tempPaths.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var path in _tempPaths)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                /* best-effort cleanup */
            }
        }
    }
}
