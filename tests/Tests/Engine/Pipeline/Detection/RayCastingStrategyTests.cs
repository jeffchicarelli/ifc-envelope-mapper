using IfcEnvelopeMapper.Engine.Pipeline.Detection;

namespace IfcEnvelopeMapper.Tests.Engine.Pipeline.Detection;

/// <summary>
/// Determinism tests for <see cref="RayCastingStrategy"/>. The integration
/// tests pin golden TP/FP/FN/TN counts; this class focuses narrowly on the
/// reproducibility contract — same inputs, same seed, same classifications,
/// same order. Without this, a future change to the RNG seeding (e.g. moving
/// to <c>Random.Shared</c> for "performance") would silently break the
/// dissertation's reproducibility claim.
/// </summary>
[Trait("Category", "Integration")]
public sealed class RayCastingStrategyTests : IfcTestBase
{
    public RayCastingStrategyTests() : base("duplex.ifc") { }

    [Fact]
    public void Detect_SameElementsTwice_ProducesIdenticalClassifications()
    {
        var elements = Model.Elements;

        var first  = new RayCastingStrategy().Detect(elements);
        var second = new RayCastingStrategy().Detect(elements);

        first.Classifications.Count.Should().Be(second.Classifications.Count);

        // Pair classifications by GlobalId — Detect() doesn't guarantee
        // ordering, but per-element verdicts must match.
        var firstByGid  = first.Classifications.ToDictionary(c => c.Element.GlobalId, c => c.IsExterior);
        var secondByGid = second.Classifications.ToDictionary(c => c.Element.GlobalId, c => c.IsExterior);

        firstByGid.Should().Equal(secondByGid);
    }

    [Fact]
    public void Detect_ProducesOneClassificationPerInputElement()
    {
        var elements = Model.Elements;

        var result = new RayCastingStrategy().Detect(elements);

        result.Classifications.Count.Should().Be(elements.Count);

        // Every input GlobalId must be represented exactly once.
        var inputIds  = elements.Select(e => e.GlobalId).OrderBy(s => s, StringComparer.Ordinal).ToList();
        var outputIds = result.Classifications.Select(c => c.Element.GlobalId).OrderBy(s => s, StringComparer.Ordinal).ToList();
        outputIds.Should().Equal(inputIds);
    }

    [Fact]
    public void Detect_EmptyInput_ReturnsEmptyResult()
    {
        var result = new RayCastingStrategy().Detect(Array.Empty<IfcEnvelopeMapper.Ifc.Domain.Element>());

        result.Classifications.Should().BeEmpty();
    }

    [Fact]
    public void Detect_DifferentRayCount_CanProduceDifferentVerdicts()
    {
        // Sanity check that the RayCounts parameter has measurable effect —
        // guards against the constructor parameter quietly being ignored.
        // We don't assert which verdicts differ (depends on geometry), only
        // that the *configuration* is honoured by checking determinism is
        // per-instance, not global.
        var elements = Model.Elements;

        var sparse = new RayCastingStrategy(numRays: 2).Detect(elements);
        var dense  = new RayCastingStrategy(numRays: 16).Detect(elements);

        sparse.Classifications.Count.Should().Be(elements.Count);
        dense.Classifications.Count.Should().Be(elements.Count);

        // The two runs each have their own RNG with the same seed, so the
        // specific verdicts differ for at least one element on a non-trivial
        // model — duplex has enough geometry to satisfy this.
        var sparseByGid = sparse.Classifications.ToDictionary(c => c.Element.GlobalId, c => c.IsExterior);
        var denseByGid  = dense.Classifications.ToDictionary(c => c.Element.GlobalId, c => c.IsExterior);

        var anyDiffer = sparseByGid.Any(kvp => denseByGid[kvp.Key] != kvp.Value);
        anyDiffer.Should().BeTrue("numRays must influence ray-count-dependent classifications");
    }
}
