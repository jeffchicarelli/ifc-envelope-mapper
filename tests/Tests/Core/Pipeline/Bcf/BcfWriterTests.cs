using System.IO.Compression;
using System.Xml.Linq;
using g4;
using IfcEnvelopeMapper.Core.Pipeline.Bcf;

namespace IfcEnvelopeMapper.Tests.Core.Pipeline.Bcf;

public sealed class BcfWriterTests : IDisposable
{
    private readonly List<string> _tempPaths = new();

    [Fact]
    public void Write_ProducesExpectedZipEntries()
    {
        // Arrange
        var report = MakeReport(("a-id", "a-title"), ("b-id", "b-title"));
        var path   = NewTempPath();

        // Act
        BcfWriter.Write(report, path);

        // Assert
        using var zip   = ZipFile.OpenRead(path);
        var       names = zip.Entries.Select(e => e.FullName).ToList();
        names.Should().Contain("bcf.version");
        foreach (var topic in report.Topics)
        {
            names.Should().Contain($"{topic.TopicGuid}/markup.bcf");
            names.Should().Contain($"{topic.TopicGuid}/viewpoint.bcfv");
        }
    }

    [Fact]
    public void Write_MarkupBcf_ContainsTopicGuidAndTitle()
    {
        // Arrange
        var report = MakeReport(("element-1", "Custom Title"));
        var topic  = report.Topics.Single();
        var path   = NewTempPath();

        // Act
        BcfWriter.Write(report, path);

        // Assert
        using var zip    = ZipFile.OpenRead(path);
        var       entry  = zip.GetEntry($"{topic.TopicGuid}/markup.bcf")!;
        using var stream = entry.Open();
        var       doc    = XDocument.Load(stream);

        var topicEl = doc.Element("Markup")!.Element("Topic")!;
        topicEl.Attribute("Guid")!.Value.Should().Be(topic.TopicGuid.ToString());
        topicEl.Element("Title")!.Value.Should().Be("Custom Title");
    }

    [Fact]
    public void Write_ViewpointBcfv_ReferencesIfcGuid()
    {
        // Arrange
        var report = MakeReport(("ifc-target-123", "t"));
        var topic  = report.Topics.Single();
        var path   = NewTempPath();

        // Act
        BcfWriter.Write(report, path);

        // Assert
        using var zip    = ZipFile.OpenRead(path);
        var       entry  = zip.GetEntry($"{topic.TopicGuid}/viewpoint.bcfv")!;
        using var stream = entry.Open();
        var       doc    = XDocument.Load(stream);

        var component = doc.Element("VisualizationInfo")!
            .Element("Components")!
            .Element("Selection")!
            .Element("Component")!;
        component.Attribute("IfcGuid")!.Value.Should().Be("ifc-target-123");
    }

    private static BcfReport MakeReport(params (string IfcGuid, string Title)[] specs)
    {
        var camera = new BcfCamera(
            Position:    new Vector3d(0, -5, 1.5),
            Direction:   new Vector3d(0, 1, -0.3).Normalized,
            UpVector:    new Vector3d(0, 0, 1),
            FieldOfView: 60.0);

        var viewpoint = new BcfViewpoint(
            ViewpointGuid: Guid.NewGuid(),
            IfcGuid:       specs.First().IfcGuid,
            Camera:        camera);

        var topics = specs.Select(s => new BcfTopic(
                TopicGuid:      Guid.NewGuid(),
                Title:          s.Title,
                TopicType:      "Test",
                TopicStatus:    "Open",
                CreationDate:   DateTimeOffset.UtcNow,
                CreationAuthor: "test",
                Viewpoint:      viewpoint)).ToList();

        return new BcfReport("2.1", topics);
    }

    private string NewTempPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"bcf-test-{Guid.NewGuid():N}.bcf");
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
