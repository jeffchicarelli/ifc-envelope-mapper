using g4;
using IfcEnvelopeMapper.Application.Evaluation;
using IfcEnvelopeMapper.Domain.Detection;
using IfcEnvelopeMapper.Domain.Evaluation;
using IfcEnvelopeMapper.Domain.Interfaces;
using IfcEnvelopeMapper.Domain.Surface;

namespace IfcEnvelopeMapper.Infrastructure.Tests.Evaluation;

public sealed class MetricsCalculatorTests
{
    [Fact]
    public void Compute_AllFourBuckets_ReturnsExpectedCounts()
    {
        var (eTp, eFp, eFn, eTn) = (Elem(0), Elem(1), Elem(2), Elem(3));

        var classifications = new[] { Classify(eTp, true), Classify(eFp, true), Classify(eFn, false), Classify(eTn, false) };

        var groundTruth = new[]
        {
            new GroundTruthRecord(eTp.GlobalId, true,  null),
            new GroundTruthRecord(eFp.GlobalId, false, null),
            new GroundTruthRecord(eFn.GlobalId, true,  null),
            new GroundTruthRecord(eTn.GlobalId, false, null)
        };

        var counts = MetricsCalculator.Compute(classifications, groundTruth);

        counts.TruePositives.Should().Be(1);
        counts.FalsePositives.Should().Be(1);
        counts.FalseNegatives.Should().Be(1);
        counts.TrueNegatives.Should().Be(1);
    }

    [Fact]
    public void Compute_GroundTruthWithNullLabel_IsExcludedFromCounts()
    {
        var element = Elem(0);

        var classifications = new[] { Classify(element, true) };
        var groundTruth = new[] { new GroundTruthRecord(element.GlobalId, null, null) };

        var counts = MetricsCalculator.Compute(classifications, groundTruth);

        counts.Total.Should().Be(0);
    }

    [Fact]
    public void Compute_ClassificationWithNoMatchingGroundTruth_IsSkipped()
    {
        var element = Elem(0);

        var classifications = new[] { Classify(element, true) };
        var groundTruth = Array.Empty<GroundTruthRecord>();

        var counts = MetricsCalculator.Compute(classifications, groundTruth);

        counts.Total.Should().Be(0);
    }

    [Fact]
    public void Compute_EmptyInputs_ReturnsAllZeroes()
    {
        var counts = MetricsCalculator.Compute(Array.Empty<ElementClassification>(), Array.Empty<GroundTruthRecord>());

        counts.TruePositives.Should().Be(0);
        counts.FalsePositives.Should().Be(0);
        counts.FalseNegatives.Should().Be(0);
        counts.TrueNegatives.Should().Be(0);
        counts.Total.Should().Be(0);

        double.IsNaN(counts.Precision).Should().BeTrue();
        double.IsNaN(counts.Recall).Should().BeTrue();
    }

    [Fact]
    public void Compute_AllPositiveCorrect_PrecisionAndRecallAreOne()
    {
        var elements = Enumerable.Range(0, 5).Select(Elem).ToList();

        var classifications = elements.Select(e => Classify(e, true)).ToList();
        var groundTruth = elements.Select(e => new GroundTruthRecord(e.GlobalId, true, null)).ToList();

        var counts = MetricsCalculator.Compute(classifications, groundTruth);

        counts.TruePositives.Should().Be(5);
        counts.Precision.Should().Be(1.0);
        counts.Recall.Should().Be(1.0);
    }

    [Fact]
    public void Compute_OnlyFalsePositives_PrecisionIsZero_RecallIsNaN()
    {
        var elements = Enumerable.Range(0, 3).Select(Elem).ToList();

        var classifications = elements.Select(e => Classify(e, true)).ToList();
        var groundTruth = elements.Select(e => new GroundTruthRecord(e.GlobalId, false, null)).ToList();

        var counts = MetricsCalculator.Compute(classifications, groundTruth);

        counts.FalsePositives.Should().Be(3);
        counts.Precision.Should().Be(0.0);
        double.IsNaN(counts.Recall).Should().BeTrue();
    }

    private static IElement Elem(int index) => new StubElement($"guid-{index:D4}");

    private static ElementClassification Classify(IElement element, bool isExterior)
        => new(element, isExterior, Array.Empty<Face>());

    private sealed class StubElement(string globalId) : IElement
    {
        public string GlobalId => globalId;
        public string? Name => null;
        public AxisAlignedBox3d GetBoundingBox() => default;
        public DMesh3 GetMesh() => new DMesh3();

        /// <summary>IFC schema class name (e.g. "IfcWall", "IfcSlab").</summary>
        public string IfcType { get; }
    }
}
