namespace IfcEnvelopeMapper.Domain.Evaluation;

/// <summary>
/// Confusion-matrix counts for a single detection run against a ground-truth set.
/// <code>
///                    ┌────────────────┬────────────────┐
///                    │ predicted: yes │ predicted: no  │
///     ┌──────────────┼────────────────┼────────────────┤
///     │ actual: yes  │       TP       │       FN       │
///     ├──────────────┼────────────────┼────────────────┤
///     │ actual: no   │       FP       │       TN       │
///     └──────────────┴────────────────┴────────────────┘
/// </code>
/// Precision answers "of what we flagged as facade, how much actually is?";
/// Recall answers "of the real facade, how much did we catch?".
/// Both return <see cref="double.NaN"/> when their denominator is zero.
/// </summary>
public sealed record DetectionCounts(int TruePositives, int FalsePositives, int FalseNegatives, int TrueNegatives)
{
    /// <summary>Total number of labelled elements considered (TP + FP + FN + TN).</summary>
    public int Total => TruePositives + FalsePositives + FalseNegatives + TrueNegatives;

    /// <summary>
    /// TP / (TP + FP) — fraction of predicted positives that are correct.
    /// Returns <see cref="double.NaN"/> when no elements were predicted exterior.
    /// </summary>
    public double Precision => TruePositives + FalsePositives == 0
        ? double.NaN
        : (double)TruePositives / (TruePositives + FalsePositives);

    /// <summary>
    /// TP / (TP + FN) — fraction of true positives retrieved.
    /// Returns <see cref="double.NaN"/> when no elements are actually exterior.
    /// </summary>
    public double Recall => TruePositives + FalseNegatives == 0
        ? double.NaN
        : (double)TruePositives / (TruePositives + FalseNegatives);
}
