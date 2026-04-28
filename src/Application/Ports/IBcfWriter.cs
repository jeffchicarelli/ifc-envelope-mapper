using IfcEnvelopeMapper.Application.Reports;

namespace IfcEnvelopeMapper.Application.Ports;

/// <summary>
/// Serialises a <see cref="BcfPackage"/> to a BCF 2.1 ZIP archive on disk.
/// </summary>
public interface IBcfWriter
{
    /// <summary>Writes <paramref name="package"/> as a BCF 2.1 ZIP archive to <paramref name="outputPath"/>.</summary>
    void Write(BcfPackage package, string outputPath);
}
