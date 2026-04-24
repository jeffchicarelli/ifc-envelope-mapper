using IfcEnvelopeMapper.Core.Pipeline.Detection;

namespace IfcEnvelopeMapper.Core.Pipeline.Evaluation;

public sealed record EvaluationResult(
    DetectionResult Detection,
    DetectionCounts Counts,
    IReadOnlyList<GroundTruthRecord> GroundTruth);
