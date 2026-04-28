using System.Text.Json;
using IfcEnvelopeMapper.Application.Ports;
using IfcEnvelopeMapper.Application.Reports;

namespace IfcEnvelopeMapper.Infrastructure.Persistence;

/// <summary>
/// Serialises a <see cref="DetectionReport"/> to disk as indented JSON with
/// camelCase property names (matches the JavaScript ecosystem the dissertation
/// viewer code is in). Creates the parent directory if it doesn't exist.
/// </summary>
public sealed class JsonReportWriter : IJsonReportWriter
{
    private static readonly JsonSerializerOptions OPTIONS = new()
    {
        WriteIndented        = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public void Write(DetectionReport report, string outputPath)
    {
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var json = JsonSerializer.Serialize(report, OPTIONS);
        File.WriteAllText(outputPath, json);
    }
}
