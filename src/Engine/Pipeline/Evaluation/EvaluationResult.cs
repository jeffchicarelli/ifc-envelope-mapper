using IfcEnvelopeMapper.Engine.Pipeline.Detection;

namespace IfcEnvelopeMapper.Engine.Pipeline.Evaluation;

/// <summary>
/// Output of one evaluation run: the detection output, the confusion-matrix
/// <see cref="DetectionCounts"/>, and the ground-truth records the counts were
/// computed against.
/// </summary>
public sealed record EvaluationResult(
    DetectionResult Detection,
    DetectionCounts Counts,
    IReadOnlyList<GroundTruthRecord> GroundTruth);
