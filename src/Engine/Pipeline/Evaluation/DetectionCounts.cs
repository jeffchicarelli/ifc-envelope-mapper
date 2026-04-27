namespace IfcEnvelopeMapper.Engine.Pipeline.Evaluation;

/// <summary>
/// Confusion-matrix counts for a single detection run against a ground-truth set.
///
///                    ┌────────────────┬────────────────┐
///                    │ predicted: yes │ predicted: no  │
///     ┌──────────────┼────────────────┼────────────────┤
///     │ actual: yes  │       TP       │       FN       │
///     ├──────────────┼────────────────┼────────────────┤
///     │ actual: no   │       FP       │       TN       │
///     └──────────────┴────────────────┴────────────────┘
///
/// Precision answers "of what we flagged as facade, how much actually is?";
/// Recall answers "of the real facade, how much did we catch?".
/// Both return <see cref="double.NaN"/> when their denominator is zero.
/// </summary>
public sealed record DetectionCounts(
    int TruePositives,
    int FalsePositives,
    int FalseNegatives,
    int TrueNegatives)
{
    public int Total => TruePositives + FalsePositives + FalseNegatives + TrueNegatives;

    public double Precision => TruePositives + FalsePositives == 0
        ? double.NaN
        : (double)TruePositives / (TruePositives + FalsePositives);

    public double Recall => TruePositives + FalseNegatives == 0
        ? double.NaN
        : (double)TruePositives / (TruePositives + FalseNegatives);
}
