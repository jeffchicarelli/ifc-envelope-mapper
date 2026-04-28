using System.Text.Json;
using IfcEnvelopeMapper.Application.Ports;
using IfcEnvelopeMapper.Application.Reports;

namespace IfcEnvelopeMapper.Infrastructure.Persistence;

/// <summary>Serialises a <see cref="DetectionReport"/> to disk as indented JSON with camelCase property names. Creates the parent directory if absent.</summary>
public sealed class JsonReportWriter : IJsonReportWriter
{
    private static readonly JsonSerializerOptions _options = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <inheritdoc/>
    public void Write(DetectionReport report, string outputPath)
    {
        var dir = Path.GetDirectoryName(outputPath);

        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var json = JsonSerializer.Serialize(report, _options);

        File.WriteAllText(outputPath, json);
    }
}
