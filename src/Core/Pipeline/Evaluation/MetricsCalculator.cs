using IfcEnvelopeMapper.Core.Pipeline.Detection;

namespace IfcEnvelopeMapper.Core.Pipeline.Evaluation;

/// <summary>
/// Turns a set of predicted <see cref="ElementClassification"/>s plus a ground-truth
/// table into <see cref="DetectionCounts"/>. Only records with an explicit true/false
/// label contribute; <c>IsExterior == null</c> rows are skipped. Classifications
/// whose <c>GlobalId</c> is not present in the ground truth are also skipped.
/// </summary>
public static class MetricsCalculator
{
    public static DetectionCounts Compute(
        IReadOnlyList<ElementClassification> classifications,
        IReadOnlyList<GroundTruthRecord> groundTruth)
    {
        var gtByGlobalId = groundTruth
            .Where(r => r.IsExterior.HasValue)
            .ToDictionary(
                r => r.GlobalId,
                r => r.IsExterior!.Value,
                StringComparer.Ordinal);

        var tp = 0;
        var fp = 0;
        var fn = 0;
        var tn = 0;

        foreach (var c in classifications)
        {
            if (!gtByGlobalId.TryGetValue(c.Element.GlobalId, out var gtIsExterior))
            {
                continue;
            }

            if (c.IsExterior && gtIsExterior) tp++;
            else if (c.IsExterior && !gtIsExterior) fp++;
            else if (!c.IsExterior && gtIsExterior) fn++;
            else tn++;
        }

        return new DetectionCounts(tp, fp, fn, tn);
    }
}
