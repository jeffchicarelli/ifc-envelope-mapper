using g4;
using IfcEnvelopeMapper.Core.Domain.Element;
using IfcEnvelopeMapper.Core.Domain.Surface;
using IfcEnvelopeMapper.Core.Pipeline.Detection;
using IfcEnvelopeMapper.Core.Pipeline.Evaluation;

namespace IfcEnvelopeMapper.Tests.Core.Pipeline.Evaluation;

public class MetricsCalculatorTests
{
    private static BuildingElement MakeElement(string id) => new()
    {
        GlobalId = id,
        IfcType = "IfcWall",
        Mesh = new DMesh3(),
    };

    private static ElementClassification Classify(string id, bool isExterior) =>
        new(MakeElement(id), isExterior, Array.Empty<Face>());

    [Fact]
    public void Compute_BothListsEmpty_ReturnsAllZeros()
    {
        var counts = MetricsCalculator.Compute(
            Array.Empty<ElementClassification>(),
            Array.Empty<GroundTruthRecord>());

        counts.Should().Be(new DetectionCounts(0, 0, 0, 0));
    }

    [Fact]
    public void Compute_PerfectMatch_AllTruePositives()
    {
        var classifications = new[]
        {
            Classify("a", isExterior: true),
            Classify("b", isExterior: true),
        };
        var groundTruth = new[]
        {
            new GroundTruthRecord("a", IsExterior: true, Note: null),
            new GroundTruthRecord("b", IsExterior: true, Note: null),
        };

        var counts = MetricsCalculator.Compute(classifications, groundTruth);

        counts.Should().Be(new DetectionCounts(TruePositives: 2, FalsePositives: 0, FalseNegatives: 0, TrueNegatives: 0));
    }

    [Fact]
    public void Compute_MixedOutcomes_CountsEachBucketCorrectly()
    {
        // tp  — predicted ext, actually ext
        // fp  — predicted ext, actually int
        // fn  — predicted int, actually ext
        // tn  — predicted int, actually int
        var classifications = new[]
        {
            Classify("tp", isExterior: true),
            Classify("fp", isExterior: true),
            Classify("fn", isExterior: false),
            Classify("tn", isExterior: false),
        };
        var groundTruth = new[]
        {
            new GroundTruthRecord("tp", true,  null),
            new GroundTruthRecord("fp", false, null),
            new GroundTruthRecord("fn", true,  null),
            new GroundTruthRecord("tn", false, null),
        };

        var counts = MetricsCalculator.Compute(classifications, groundTruth);

        counts.Should().Be(new DetectionCounts(1, 1, 1, 1));
    }

    [Fact]
    public void Compute_GroundTruthWithNullLabel_IsExcluded()
    {
        // "unknown" row must not contribute — otherwise it would count as tn here.
        var classifications = new[] { Classify("a", isExterior: false) };
        var groundTruth = new[]
        {
            new GroundTruthRecord("a", IsExterior: null, Note: "not curated yet"),
        };

        var counts = MetricsCalculator.Compute(classifications, groundTruth);

        counts.Should().Be(new DetectionCounts(0, 0, 0, 0));
    }

    [Fact]
    public void Compute_ClassificationNotInGroundTruth_IsSkipped()
    {
        // "ghost" has no matching ground-truth row — predicted but unlabeled.
        var classifications = new[]
        {
            Classify("known", isExterior: true),
            Classify("ghost", isExterior: true),
        };
        var groundTruth = new[]
        {
            new GroundTruthRecord("known", IsExterior: true, Note: null),
        };

        var counts = MetricsCalculator.Compute(classifications, groundTruth);

        counts.Should().Be(new DetectionCounts(1, 0, 0, 0));
    }

    [Fact]
    public void Compute_GroundTruthEntryWithoutClassification_IsSkipped()
    {
        // gt contains ids the detector never saw — they must not become false negatives
        // (the loop is driven by the classification list, not the gt list).
        var classifications = Array.Empty<ElementClassification>();
        var groundTruth = new[]
        {
            new GroundTruthRecord("a", IsExterior: true, Note: null),
            new GroundTruthRecord("b", IsExterior: false, Note: null),
        };

        var counts = MetricsCalculator.Compute(classifications, groundTruth);

        counts.Should().Be(new DetectionCounts(0, 0, 0, 0));
    }

    [Fact]
    public void Compute_GlobalIdMatchingIsCaseSensitive()
    {
        // Dictionary uses StringComparer.Ordinal → "A" and "a" must NOT match.
        var classifications = new[] { Classify("A", isExterior: true) };
        var groundTruth = new[] { new GroundTruthRecord("a", IsExterior: true, Note: null) };

        var counts = MetricsCalculator.Compute(classifications, groundTruth);

        counts.Should().Be(new DetectionCounts(0, 0, 0, 0));
    }

    [Fact]
    public void Compute_AllFalsePositives_OnlyFpBucketIncremented()
    {
        var classifications = new[]
        {
            Classify("x", isExterior: true),
            Classify("y", isExterior: true),
        };
        var groundTruth = new[]
        {
            new GroundTruthRecord("x", IsExterior: false, Note: null),
            new GroundTruthRecord("y", IsExterior: false, Note: null),
        };

        var counts = MetricsCalculator.Compute(classifications, groundTruth);

        counts.Should().Be(new DetectionCounts(0, 2, 0, 0));
    }

    [Fact]
    public void Compute_AllFalseNegatives_OnlyFnBucketIncremented()
    {
        var classifications = new[]
        {
            Classify("x", isExterior: false),
            Classify("y", isExterior: false),
        };
        var groundTruth = new[]
        {
            new GroundTruthRecord("x", IsExterior: true, Note: null),
            new GroundTruthRecord("y", IsExterior: true, Note: null),
        };

        var counts = MetricsCalculator.Compute(classifications, groundTruth);

        counts.Should().Be(new DetectionCounts(0, 0, 2, 0));
    }

    [Fact]
    public void Compute_DuplicateClassificationForSameId_BothContribute()
    {
        // Guards against silent deduplication. If the caller passes duplicates,
        // the counter reflects them — the fix belongs at the detector, not here.
        var classifications = new[]
        {
            Classify("a", isExterior: true),
            Classify("a", isExterior: true),
        };
        var groundTruth = new[] { new GroundTruthRecord("a", IsExterior: true, Note: null) };

        var counts = MetricsCalculator.Compute(classifications, groundTruth);

        counts.TruePositives.Should().Be(2);
    }
}
