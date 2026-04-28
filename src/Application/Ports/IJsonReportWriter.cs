using IfcEnvelopeMapper.Application.Reports;

namespace IfcEnvelopeMapper.Application.Ports;

/// <summary>
/// Serialises a <see cref="DetectionReport"/> to indented JSON on disk.
/// </summary>
public interface IJsonReportWriter
{
    void Write(DetectionReport report, string outputPath);
}
