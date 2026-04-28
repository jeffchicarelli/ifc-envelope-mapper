using IfcEnvelopeMapper.Engine.Pipeline.Detection;
using IfcEnvelopeMapper.Ifc.Domain;
using IfcEnvelopeMapper.Ifc.Domain.Surface;

namespace IfcEnvelopeMapper.Tests.Engine.Pipeline.Detection;

/// <summary>
/// Contract and determinism tests for <see cref="VoxelFloodFillStrategy"/>.
/// End-to-end precision/recall against ground truth lives in
/// <c>EvaluationPipelineTests</c>; this class focuses on the
/// <see cref="IEnvelopeDetector"/> contract and the strategy-internal
/// invariants that integration tests would not catch (empty input,
/// face-extractor invocation pattern, envelope aggregation).
/// </summary>
[Trait("Category", "Integration")]
public sealed class VoxelFloodFillStrategyTests : IfcTestBase
{
    public VoxelFloodFillStrategyTests() : base("airwell/control.ifc") { }

    [Fact]
    public void Detect_EmptyInput_ReturnsEmptyResult()
    {
        var result = new VoxelFloodFillStrategy().Detect(Array.Empty<Element>());

        result.Classifications.Should().BeEmpty();
        result.Envelope.Faces.Should().BeEmpty();
    }

    [Fact]
    public void Detect_ProducesOneClassificationPerInputElement()
    {
        var elements = Model.Elements;

        var result = new VoxelFloodFillStrategy().Detect(elements);

        result.Classifications.Count.Should().Be(elements.Count);

        // Sort both sides — Detect() ordering is not part of the contract.
        var inputIds  = elements.Select(e => e.GlobalId).OrderBy(s => s, StringComparer.Ordinal).ToList();
        var outputIds = result.Classifications.Select(c => c.Element.GlobalId).OrderBy(s => s, StringComparer.Ordinal).ToList();
        outputIds.Should().Equal(inputIds);
    }

    [Fact]
    public void Detect_SameInputTwice_ProducesIdenticalClassifications()
    {
        var elements = Model.Elements;

        var first  = new VoxelFloodFillStrategy().Detect(elements);
        var second = new VoxelFloodFillStrategy().Detect(elements);

        first.Classifications.Count.Should().Be(second.Classifications.Count);

        // Pair by GlobalId — verdict must match per element regardless of order.
        var firstByGid  = first.Classifications.ToDictionary(c => c.Element.GlobalId, c => c.IsExterior);
        var secondByGid = second.Classifications.ToDictionary(c => c.Element.GlobalId, c => c.IsExterior);

        firstByGid.Should().Equal(secondByGid);
    }

    [Fact]
    public void Detect_ExteriorElements_HaveNonEmptyFaces_InteriorElements_HaveEmptyFaces()
    {
        // Couples IsExterior and ExternalFaces: face extraction runs iff the
        // element is exterior. A bug running it unconditionally (or never)
        // would still satisfy the cardinality test but break this invariant.
        var result = new VoxelFloodFillStrategy().Detect(Model.Elements);

        foreach (var c in result.Classifications)
        {
            if (c.IsExterior)
            {
                c.ExternalFaces.Should().NotBeEmpty(
                    "exterior element {0} must have extracted faces", c.Element.GlobalId);
            }
            else
            {
                c.ExternalFaces.Should().BeEmpty(
                    "interior element {0} must not carry faces", c.Element.GlobalId);
            }
        }
    }

    [Fact]
    public void Detect_ExteriorFaces_AreAggregatedIntoEnvelope()
    {
        var result = new VoxelFloodFillStrategy().Detect(Model.Elements);

        var expected = result.Classifications
                             .Where(c => c.IsExterior)
                             .SelectMany(c => c.ExternalFaces)
                             .Count();

        result.Envelope.Faces.Count.Should().Be(expected);
    }

    [Fact]
    public void Detect_FaceExtractor_OnlyInvokedForExteriorElements()
    {
        // Face extraction is the expensive per-element cost the strategy
        // deliberately skips for interior elements. Verify with a recording
        // double — without a mocking library, a hand-rolled stub is the pattern.
        var recorder = new RecordingFaceExtractor();

        var result = new VoxelFloodFillStrategy(faceExtractor: recorder).Detect(Model.Elements);

        var exteriorIds = result.Classifications
                                .Where(c => c.IsExterior)
                                .Select(c => c.Element.GlobalId)
                                .ToHashSet(StringComparer.Ordinal);

        recorder.CalledFor.Should().BeEquivalentTo(exteriorIds);
    }

    [Fact]
    public void Detect_OnRealModel_ClassifiesAtLeastOneElementAsExterior()
    {
        // Smoke test that flood-fill reaches the model shell. If BuildGrid's
        // padding is wrong (corner voxel ends up inside the model) zero elements
        // would be classified exterior — catastrophic but otherwise silent.
        var result = new VoxelFloodFillStrategy().Detect(Model.Elements);

        result.Classifications.Any(c => c.IsExterior).Should().BeTrue();
    }

    [Fact]
    public void Detect_DifferentVoxelSize_DoesNotThrow_AndStillProducesClassifications()
    {
        // Sanity check that the constructor parameter actually reaches BuildGrid.
        // Larger voxel (1.0 m) is the safe direction — smaller multiplies cost ~8x.
        var result = new VoxelFloodFillStrategy(voxelSize: 1.0).Detect(Model.Elements);

        result.Classifications.Count.Should().Be(Model.Elements.Count);
        result.Classifications.Any(c => c.IsExterior).Should().BeTrue();
    }

    /// <summary>
    /// Records which <see cref="Element.GlobalId"/>s the strategy invokes
    /// extraction for, while delegating to the real <see cref="PcaFaceExtractor"/>
    /// so the rest of the pipeline observes a populated envelope.
    /// </summary>
    private sealed class RecordingFaceExtractor : IFaceExtractor
    {
        private readonly IFaceExtractor _inner = new PcaFaceExtractor();
        public HashSet<string> CalledFor { get; } = new(StringComparer.Ordinal);

        public IReadOnlyList<Face> Extract(Element element)
        {
            CalledFor.Add(element.GlobalId);
            return _inner.Extract(element);
        }
    }
}
