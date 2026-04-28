using IfcEnvelopeMapper.Application.Ports;
using IfcEnvelopeMapper.Domain.Detection;
using IfcEnvelopeMapper.Domain.Evaluation;
using IfcEnvelopeMapper.Domain.Services;

namespace IfcEnvelopeMapper.Application.Evaluation;

/// <summary>
/// End-to-end evaluation for a single IFC + ground-truth pair:
/// <c>load → detect → read GT → compute metrics</c>.
/// Returns the full <see cref="EvaluationResult"/> so callers can print summary
/// stats and also drill into individual classifications (e.g., tests that
/// visualize false positives/negatives via <c>GeometryDebug</c>).
/// <para>The ground-truth CSV must already exist. To bootstrap it for a new
/// model, call <c>GroundTruthGenerator.GenerateFromIfc</c> first.</para>
/// </summary>
public static class EvaluationService
{
    public static EvaluationResult EvaluateDetection(
        string ifcPath,
        string groundTruthPath,
        IEnvelopeDetector strategy,
        IModelLoader loader,
        IGroundTruthReader groundTruthReader)
    {
        using var model = loader.Load(ifcPath);

        var detection   = strategy.Detect(model.Elements);
        var groundTruth = groundTruthReader.Read(groundTruthPath);
        var counts      = MetricsCalculator.Compute(detection.Classifications, groundTruth);

        return new EvaluationResult(detection, counts, groundTruth);
    }
}
