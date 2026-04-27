using System.Text.Json;
using IfcEnvelopeMapper.Engine.Pipeline.Reporting;

namespace IfcEnvelopeMapper.Tests.Engine.Pipeline.Reporting;

public sealed class JsonReportWriterTests : IDisposable
{
    private readonly List<string> _tempPaths = new();

    [Fact]
    public void Write_RoundTripsThroughJsonSerializer()
    {
        // Arrange
        var report = MakeSampleReport();
        var path   = NewTempPath();

        // Act
        JsonReportWriter.Write(report, path);
        var json    = File.ReadAllText(path);
        var parsed  = JsonSerializer.Deserialize<DetectionReport>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });

        // Assert — record + collection equivalence (FluentAssertions deep-compares)
        parsed.Should().BeEquivalentTo(report);
    }

    [Fact]
    public void Write_CreatesMissingParentDirectory()
    {
        // Arrange — nest two levels deep, neither directory exists
        var nestedDir = Path.Combine(
            Path.GetTempPath(),
            $"json-report-test-{Guid.NewGuid():N}",
            "subdir");
        var path = Path.Combine(nestedDir, "report.json");
        _tempPaths.Add(Path.GetDirectoryName(nestedDir)!); // remember the top-level for cleanup

        // Act
        JsonReportWriter.Write(MakeSampleReport(), path);

        // Assert
        File.Exists(path).Should().BeTrue();
    }

    [Fact]
    public void Write_OutputIsIndentedCamelCaseJson()
    {
        // Arrange
        var report = MakeSampleReport();
        var path   = NewTempPath();

        // Act
        JsonReportWriter.Write(report, path);
        var json = File.ReadAllText(path);

        // Assert — camelCase property names + indentation visible in raw text
        json.Should().Contain("\"schemaVersion\"");
        json.Should().Contain("\"exteriorCount\"");
        json.Should().NotContain("\"SchemaVersion\""); // no PascalCase leak
        json.Should().Contain("\n");                    // indented (multi-line)
        json.Should().Contain("  ");                    // indented (two-space body)
    }

    private static DetectionReport MakeSampleReport()
    {
        return new DetectionReport(
            SchemaVersion:   "1",
            Input:           "/x/duplex.ifc",
            Strategy:        "voxel",
            Config:          new StrategyConfig(VoxelSize: 0.25, NumRays: null, JitterDeg: null, HitRatio: null),
            ExteriorCount:   2,
            InteriorCount:   1,
            Elements:        new[]
            {
                new ElementReport("a", "IfcWall",   true),
                new ElementReport("b", "IfcSlab",   true),
                new ElementReport("c", "IfcMember", false),
            },
            GeneratedAt:     new DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero),
            DurationSeconds: 1.234);
    }

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
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
                else if (File.Exists(path))
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
