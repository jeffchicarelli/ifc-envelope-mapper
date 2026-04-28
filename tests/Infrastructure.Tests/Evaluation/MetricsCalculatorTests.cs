using IfcEnvelopeMapper.Application.Evaluation;
using IfcEnvelopeMapper.Domain.Detection;
using IfcEnvelopeMapper.Domain.Evaluation;
using IfcEnvelopeMapper.Domain.Surface;
using IfcEnvelopeMapper.Infrastructure.Ifc;

namespace IfcEnvelopeMapper.Infrastructure.Tests.Evaluation;

public sealed class MetricsCalculatorTests : IfcTestBase
{
    public MetricsCalculatorTests() : base("duplex.ifc") { }

    [Fact]
    public void Compute_AllFourBuckets_ReturnsExpectedCounts()
    {
        var (eTp, eFp, eFn, eTn) = (Elem(0), Elem(1), Elem(2), Elem(3));

        var classifications = new[]
        {
            Classify(eTp, isExterior: true),
            Classify(eFp, isExterior: true),
            Classify(eFn, isExterior: false),
            Classify(eTn, isExterior: false),
        };

        var groundTruth = new[]
        {
            new GroundTruthRecord(eTp.GlobalId, IsExterior: true,  Note: null),
            new GroundTruthRecord(eFp.GlobalId, IsExterior: false, Note: null),
            new GroundTruthRecord(eFn.GlobalId, IsExterior: true,  Note: null),
            new GroundTruthRecord(eTn.GlobalId, IsExterior: false, Note: null),
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
        // IsExterior == null means "unknown" — must not contribute to any cell.
        var element = Elem(0);

        var classifications = new[] { Classify(element, isExterior: true) };
        var groundTruth     = new[] { new GroundTruthRecord(element.GlobalId, IsExterior: null, Note: null) };

        var counts = MetricsCalculator.Compute(classifications, groundTruth);

        counts.Total.Should().Be(0);
    }

    [Fact]
    public void Compute_ClassificationWithNoMatchingGroundTruth_IsSkipped()
    {
        var element = Elem(0);

        var classifications = new[] { Classify(element, isExterior: true) };
        var groundTruth     = Array.Empty<GroundTruthRecord>();

        var counts = MetricsCalculator.Compute(classifications, groundTruth);

        counts.Total.Should().Be(0);
    }

    [Fact]
    public void Compute_EmptyInputs_ReturnsAllZeroes()
    {
        var counts = MetricsCalculator.Compute(
            Array.Empty<ElementClassification>(),
            Array.Empty<GroundTruthRecord>());

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
        var elements = Model.Elements.Take(5).Select(e => (Element)e).ToList();

        var classifications = elements.Select(e => Classify(e, isExterior: true)).ToList();
        var groundTruth     = elements.Select(e => new GroundTruthRecord(e.GlobalId, true, null)).ToList();

        var counts = MetricsCalculator.Compute(classifications, groundTruth);

        counts.TruePositives.Should().Be(5);
        counts.Precision.Should().Be(1.0);
        counts.Recall.Should().Be(1.0);
    }

    [Fact]
    public void Compute_OnlyFalsePositives_PrecisionIsZero_RecallIsNaN()
    {
        var elements = Model.Elements.Take(3).Select(e => (Element)e).ToList();

        var classifications = elements.Select(e => Classify(e, isExterior: true)).ToList();
        var groundTruth     = elements.Select(e => new GroundTruthRecord(e.GlobalId, false, null)).ToList();

        var counts = MetricsCalculator.Compute(classifications, groundTruth);

        counts.FalsePositives.Should().Be(3);
        counts.Precision.Should().Be(0.0);
        double.IsNaN(counts.Recall).Should().BeTrue();
    }

    private Element Elem(int index) => (Element)Model.Elements[index];

    private static ElementClassification Classify(Element element, bool isExterior) =>
        new(element, isExterior, Array.Empty<Face>());
}
