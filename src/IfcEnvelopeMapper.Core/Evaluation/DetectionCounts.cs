namespace IfcEnvelopeMapper.Core.Evaluation;

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
