using IfcEnvelopeMapper.Domain.Evaluation;

namespace IfcEnvelopeMapper.Application.Ports;

/// <summary>
/// Reads a ground-truth label set from a persistent source (typically a CSV file).
/// </summary>
public interface IGroundTruthReader
{
    IReadOnlyList<GroundTruthRecord> Read(string path);
}
