using IfcEnvelopeMapper.Application.Ports;
using IfcEnvelopeMapper.Domain.Evaluation;
using IfcEnvelopeMapper.Domain.Services;

namespace IfcEnvelopeMapper.Application.Evaluation;

/// <summary>
/// Runs the full evaluation pipeline for one IFC + ground-truth pair: load → detect → read ground truth → compute metrics. Requires the
/// ground-truth CSV to exist; use <c>GroundTruthGenerator.GenerateFromIfc</c> to bootstrap it.
/// </summary>
public static class EvaluationService
{
    /// <summary>Runs the load → detect → read-ground-truth → compute-metrics pipeline for one IFC file and returns the result.</summary>
    public static EvaluationResult EvaluateDetection(
        string ifcPath, string groundTruthPath, IEnvelopeDetector strategy, IModelLoader loader, IGroundTruthReader groundTruthReader)
    {
        using var model = loader.Load(ifcPath);

        var detection = strategy.Detect(model.Elements);
        var groundTruth = groundTruthReader.Read(groundTruthPath);

        var counts = MetricsCalculator.Compute(detection.Classifications, groundTruth);

        return new EvaluationResult(detection, counts, groundTruth);
    }
}
