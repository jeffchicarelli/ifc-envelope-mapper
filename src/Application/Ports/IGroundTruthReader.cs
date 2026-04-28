using IfcEnvelopeMapper.Domain.Evaluation;

namespace IfcEnvelopeMapper.Application.Ports;

/// <summary>
/// Reads a ground-truth label set from a persistent source (typically a CSV file).
/// </summary>
public interface IGroundTruthReader
{
    /// <summary>Reads all labelled records from the source at <paramref name="path"/>.</summary>
    IReadOnlyList<GroundTruthRecord> Read(string path);
}
