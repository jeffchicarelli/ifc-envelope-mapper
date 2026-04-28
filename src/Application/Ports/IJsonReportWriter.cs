using IfcEnvelopeMapper.Application.Reports;

namespace IfcEnvelopeMapper.Application.Ports;

/// <summary>Serialises a <see cref="DetectionReport"/> to indented JSON on disk.</summary>
public interface IJsonReportWriter
{
    /// <summary>Serialises <paramref name="report"/> to indented JSON at <paramref name="outputPath"/>.</summary>
    void Write(DetectionReport report, string outputPath);
}
