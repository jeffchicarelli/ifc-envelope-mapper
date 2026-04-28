using IfcEnvelopeMapper.Engine.Pipeline.Detection;
using IfcEnvelopeMapper.Engine.Pipeline.Evaluation.Types;
using IfcEnvelopeMapper.Ifc.Loading;

namespace IfcEnvelopeMapper.Engine.Pipeline.Evaluation;

/// <summary>
/// End-to-end evaluation for a single IFC + ground-truth pair:
/// <c>load → detect → read GT → compute metrics</c>.
/// Returns the full <see cref="EvaluationResult"/> so callers can print summary
/// stats and also drill into individual classifications (e.g., tests that
/// visualize false positives/negatives via <c>GeometryDebug</c>).
/// <para>The ground-truth CSV must already exist. To bootstrap it for a new
/// model, call <see cref="GroundTruthGenerator.GenerateFromIfc"/> first.</para>
/// </summary>
public static class EvaluationPipeline
{
    public static EvaluationResult EvaluateDetection(
        string ifcPath,
        string groundTruthPath,
        IEnvelopeDetector strategy,
        XbimModelLoader loader)
    {
        using var model = loader.Load(ifcPath);

        var detection   = strategy.Detect(model.Elements);
        var groundTruth = GroundTruthCsvReader.Read(groundTruthPath);
        var counts      = MetricsCalculator.Compute(detection.Classifications, groundTruth);

        return new EvaluationResult(detection, counts, groundTruth);
    }
}
