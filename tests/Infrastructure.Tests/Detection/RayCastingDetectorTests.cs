using IfcEnvelopeMapper.Domain.Interfaces;
using IfcEnvelopeMapper.Infrastructure.Detection;

namespace IfcEnvelopeMapper.Infrastructure.Tests.Detection;

[Trait("Category", "Integration")]
public sealed class RayCastingDetectorTests : IfcTestBase
{
    public RayCastingDetectorTests() : base("duplex.ifc") { }

    [Fact]
    public void Detect_SameElementsTwice_ProducesIdenticalClassifications()
    {
        var elements = Model.Elements;

        var first  = new RayCastingDetector().Detect(elements);
        var second = new RayCastingDetector().Detect(elements);

        first.Classifications.Count.Should().Be(second.Classifications.Count);

        var firstByGid  = first.Classifications.ToDictionary(c => c.Element.GlobalId, c => c.IsExterior);
        var secondByGid = second.Classifications.ToDictionary(c => c.Element.GlobalId, c => c.IsExterior);

        firstByGid.Should().Equal(secondByGid);
    }

    [Fact]
    public void Detect_ProducesOneClassificationPerInputElement()
    {
        var elements = Model.Elements;

        var result = new RayCastingDetector().Detect(elements);

        result.Classifications.Count.Should().Be(elements.Count);

        var inputIds  = elements.Select(e => e.GlobalId).OrderBy(s => s, StringComparer.Ordinal).ToList();
        var outputIds = result.Classifications.Select(c => c.Element.GlobalId).OrderBy(s => s, StringComparer.Ordinal).ToList();
        outputIds.Should().Equal(inputIds);
    }

    [Fact]
    public void Detect_EmptyInput_ReturnsEmptyResult()
    {
        var result = new RayCastingDetector().Detect(Array.Empty<IElement>());

        result.Classifications.Should().BeEmpty();
    }

    [Fact]
    public void Detect_DifferentRayCount_CanProduceDifferentVerdicts()
    {
        // Guards that the numRays constructor parameter is honoured — a silent
        // ignore would make ray counts unobservable.
        var elements = Model.Elements;

        var sparse = new RayCastingDetector(numRays: 2).Detect(elements);
        var dense  = new RayCastingDetector(numRays: 16).Detect(elements);

        sparse.Classifications.Count.Should().Be(elements.Count);
        dense.Classifications.Count.Should().Be(elements.Count);

        var sparseByGid = sparse.Classifications.ToDictionary(c => c.Element.GlobalId, c => c.IsExterior);
        var denseByGid  = dense.Classifications.ToDictionary(c => c.Element.GlobalId, c => c.IsExterior);

        var anyDiffer = sparseByGid.Any(kvp => denseByGid[kvp.Key] != kvp.Value);
        anyDiffer.Should().BeTrue("numRays must influence ray-count-dependent classifications");
    }
}
