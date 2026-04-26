using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using g4;

namespace IfcEnvelopeMapper.Core.Pipeline.Bcf;

/// <summary>
/// Serialises a <see cref="BcfReport"/> to a BCF 2.1 ZIP archive.
/// Layout: <c>bcf.version</c> at root, plus one folder per topic
/// (named with the topic GUID) holding <c>markup.bcf</c> and
/// <c>viewpoint.bcfv</c>.
/// </summary>
public static class BcfWriter
{
    private static readonly XmlWriterSettings XML_SETTINGS = new()
    {
        Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        Indent   = true,
    };

    public static void Write(BcfReport report, string outputPath)
    {
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // reverse-disposal trick: zip is closed first, writing its central
        // directory into fs, then fs closes the OS handle. Flip the order
        // and the resulting archive is unreadable.
        using var fs  = File.Create(outputPath);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);

        WriteVersionEntry(zip, report.Version);
        foreach (var topic in report.Topics)
        {
            WriteTopicMarkupEntry(zip, topic);
            WriteTopicViewpointEntry(zip, topic);
        }
    }

    private static void WriteVersionEntry(ZipArchive zip, string version)
        => WriteXmlEntry(zip, "bcf.version",
            new XDocument(
                new XElement("Version",
                    new XAttribute("VersionId", version),
                    new XElement("DetailedVersion", version))));

    private static void WriteTopicMarkupEntry(ZipArchive zip, BcfTopic t)
        => WriteXmlEntry(zip, $"{t.TopicGuid}/markup.bcf",
            new XDocument(
                new XElement("Markup",
                    new XElement("Topic",
                        new XAttribute("Guid",        t.TopicGuid),
                        new XAttribute("TopicType",   t.TopicType),
                        new XAttribute("TopicStatus", t.TopicStatus),
                        new XElement("Title",          t.Title),
                        new XElement("CreationDate",   t.CreationDate.ToString("o")),
                        new XElement("CreationAuthor", t.CreationAuthor)),
                    new XElement("Viewpoints",
                        new XElement("ViewPoint",
                            new XAttribute("Guid", t.Viewpoint.ViewpointGuid),
                            new XElement("Viewpoint", "viewpoint.bcfv"))))));

    private static void WriteTopicViewpointEntry(ZipArchive zip, BcfTopic t)
    {
        var v = t.Viewpoint;
        WriteXmlEntry(zip, $"{t.TopicGuid}/viewpoint.bcfv",
            new XDocument(
                new XElement("VisualizationInfo",
                    new XAttribute("Guid", v.ViewpointGuid),
                    new XElement("Components",
                        new XElement("Selection",
                            new XElement("Component",
                                new XAttribute("IfcGuid", v.IfcGuid)))),
                    new XElement("PerspectiveCamera",
                        XmlVector("CameraViewPoint", v.Camera.Position),
                        XmlVector("CameraDirection", v.Camera.Direction),
                        XmlVector("CameraUpVector",  v.Camera.UpVector),
                        new XElement("FieldOfView",  v.Camera.FieldOfView)))));
    }

    private static XElement XmlVector(string name, Vector3d v)
        => new(name,
            new XElement("X", v.x),
            new XElement("Y", v.y),
            new XElement("Z", v.z));

    private static void WriteXmlEntry(ZipArchive zip, string entryName, XDocument doc)
    {
        var entry = zip.CreateEntry(entryName);
        using var stream = entry.Open();
        using var writer = XmlWriter.Create(stream, XML_SETTINGS);
        doc.Save(writer);
    }
}
