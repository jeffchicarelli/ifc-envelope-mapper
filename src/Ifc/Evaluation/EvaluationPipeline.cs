using IfcEnvelopeMapper.Core.Pipeline.Detection;
using IfcEnvelopeMapper.Core.Pipeline.Evaluation;
using IfcEnvelopeMapper.Ifc.Loading;

namespace IfcEnvelopeMapper.Ifc.Evaluation;

public static class EvaluationPipeline
{
    // End-to-end run for a single IFC + ground-truth pair:
    //   load → (generate GT if missing) → detect → read GT → compute metrics.
    // Returns the full EvaluationResult so callers can both print summary stats
    // AND drill into individual classifications/elements (e.g. tests visualizing
    // FP/FN with GeometryDebug).
    public static EvaluationResult EvaluateDetection(
        string ifcPath,
        string groundTruthPath,
        IDetectionStrategy strategy,
        XbimModelLoader? loader = null)
    {
        loader ??= new XbimModelLoader();
        var model = loader.Load(ifcPath);

        if (!File.Exists(groundTruthPath))
        {
            GroundTruthGenerator.GenerateFromIfc(ifcPath, groundTruthPath, model.Elements);
        }

        var detection   = strategy.Detect(model.Elements);
        var groundTruth = GroundTruthCsvReader.Read(groundTruthPath);
        var counts      = MetricsCalculator.Compute(detection.Classifications, groundTruth);

        return new EvaluationResult(detection, counts, groundTruth);
    }
}
