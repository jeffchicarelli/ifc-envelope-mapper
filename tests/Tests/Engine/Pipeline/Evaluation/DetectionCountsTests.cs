using IfcEnvelopeMapper.Engine.Pipeline.Evaluation.Types;

namespace IfcEnvelopeMapper.Tests.Engine.Pipeline.Evaluation;

public class DetectionCountsTests
{
    // ───── Total ─────

    [Fact]
    public void Total_SumsAllFourBuckets()
    {
        var counts = new DetectionCounts(TruePositives: 3, FalsePositives: 2, FalseNegatives: 4, TrueNegatives: 5);

        counts.Total.Should().Be(14);
    }

    [Fact]
    public void Total_AllZero_IsZero()
    {
        new DetectionCounts(0, 0, 0, 0).Total.Should().Be(0);
    }

    // ───── Precision ─────

    [Fact]
    public void Precision_NoPredictedPositives_ReturnsNaN()
    {
        // TP + FP = 0 → denominator is zero → undefined.
        var counts = new DetectionCounts(TruePositives: 0, FalsePositives: 0, FalseNegatives: 7, TrueNegatives: 3);

        double.IsNaN(counts.Precision).Should().BeTrue();
    }

    [Fact]
    public void Precision_PerfectPredictions_IsOne()
    {
        var counts = new DetectionCounts(TruePositives: 10, FalsePositives: 0, FalseNegatives: 2, TrueNegatives: 5);

        counts.Precision.Should().Be(1.0);
    }

    [Fact]
    public void Precision_OnlyFalsePositives_IsZero()
    {
        var counts = new DetectionCounts(TruePositives: 0, FalsePositives: 4, FalseNegatives: 0, TrueNegatives: 0);

        counts.Precision.Should().Be(0.0);
    }

    [Fact]
    public void Precision_TypicalMix_EqualsTpOverTpPlusFp()
    {
        // 3 / (3 + 1) = 0.75
        var counts = new DetectionCounts(TruePositives: 3, FalsePositives: 1, FalseNegatives: 2, TrueNegatives: 4);

        counts.Precision.Should().Be(0.75);
    }

    // ───── Recall ─────

    [Fact]
    public void Recall_NoActualPositives_ReturnsNaN()
    {
        // TP + FN = 0 → denominator is zero → undefined.
        var counts = new DetectionCounts(TruePositives: 0, FalsePositives: 2, FalseNegatives: 0, TrueNegatives: 8);

        double.IsNaN(counts.Recall).Should().BeTrue();
    }

    [Fact]
    public void Recall_PerfectRecovery_IsOne()
    {
        var counts = new DetectionCounts(TruePositives: 10, FalsePositives: 3, FalseNegatives: 0, TrueNegatives: 5);

        counts.Recall.Should().Be(1.0);
    }

    [Fact]
    public void Recall_AllMissed_IsZero()
    {
        var counts = new DetectionCounts(TruePositives: 0, FalsePositives: 0, FalseNegatives: 4, TrueNegatives: 0);

        counts.Recall.Should().Be(0.0);
    }

    [Fact]
    public void Recall_TypicalMix_EqualsTpOverTpPlusFn()
    {
        // 3 / (3 + 2) = 0.6
        var counts = new DetectionCounts(TruePositives: 3, FalsePositives: 1, FalseNegatives: 2, TrueNegatives: 4);

        counts.Recall.Should().Be(0.6);
    }

    // ───── All-zero sentinel ─────

    [Fact]
    public void AllZero_BothPrecisionAndRecallAreNaN()
    {
        var counts = new DetectionCounts(0, 0, 0, 0);

        double.IsNaN(counts.Precision).Should().BeTrue();
        double.IsNaN(counts.Recall).Should().BeTrue();
    }
}
