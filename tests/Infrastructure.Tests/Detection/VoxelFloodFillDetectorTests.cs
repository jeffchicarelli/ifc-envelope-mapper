using IfcEnvelopeMapper.Domain.Interfaces;
using IfcEnvelopeMapper.Domain.Services;
using IfcEnvelopeMapper.Domain.Surface;
using IfcEnvelopeMapper.Infrastructure.Detection;

namespace IfcEnvelopeMapper.Infrastructure.Tests.Detection;

[Trait("Category", "Integration")]
public sealed class VoxelFloodFillDetectorTests : IfcTestBase
{
    public VoxelFloodFillDetectorTests() : base("airwell/control.ifc") { }

    [Fact]
    public void Detect_EmptyInput_ReturnsEmptyResult()
    {
        var result = new VoxelFloodFillDetector().Detect(Array.Empty<IElement>());

        result.Classifications.Should().BeEmpty();
        result.Envelope.Faces.Should().BeEmpty();
    }

    [Fact]
    public void Detect_ProducesOneClassificationPerInputElement()
    {
        var elements = Model.Elements;

        var result = new VoxelFloodFillDetector().Detect(elements);

        result.Classifications.Count.Should().Be(elements.Count);

        // Sort both sides — Detect() ordering is not part of the contract.
        var inputIds = elements.Select(e => e.GlobalId).OrderBy(s => s, StringComparer.Ordinal).ToList();

        var outputIds = result.Classifications.Select(c => c.Element.GlobalId).OrderBy(s => s, StringComparer.Ordinal).ToList();

        outputIds.Should().Equal(inputIds);
    }

    [Fact]
    public void Detect_SameInputTwice_ProducesIdenticalClassifications()
    {
        var elements = Model.Elements;

        var first = new VoxelFloodFillDetector().Detect(elements);
        var second = new VoxelFloodFillDetector().Detect(elements);

        first.Classifications.Count.Should().Be(second.Classifications.Count);

        var firstByGid = first.Classifications.ToDictionary(c => c.Element.GlobalId, c => c.IsExterior);

        var secondByGid = second.Classifications.ToDictionary(c => c.Element.GlobalId, c => c.IsExterior);

        firstByGid.Should().Equal(secondByGid);
    }

    [Fact]
    public void Detect_ExteriorElements_HaveNonEmptyFaces_InteriorElements_HaveEmptyFaces()
    {
        // Face extraction runs iff the element is exterior.
        var result = new VoxelFloodFillDetector().Detect(Model.Elements);

        foreach (var c in result.Classifications)
        {
            if (c.IsExterior)
            {
                c.ExternalFaces.Should().NotBeEmpty("exterior element {0} must have extracted faces", c.Element.GlobalId);
            }
            else
            {
                c.ExternalFaces.Should().BeEmpty("interior element {0} must not carry faces", c.Element.GlobalId);
            }
        }
    }

    [Fact]
    public void Detect_ExteriorFaces_AreAggregatedIntoEnvelope()
    {
        var result = new VoxelFloodFillDetector().Detect(Model.Elements);

        var expected = result.Classifications.Where(c => c.IsExterior).SelectMany(c => c.ExternalFaces).Count();

        result.Envelope.Faces.Count.Should().Be(expected);
    }

    [Fact]
    public void Detect_FaceExtractor_OnlyInvokedForExteriorElements()
    {
        // Face extraction is the expensive per-element step the strategy deliberately
        // skips for interior elements. Verify with a hand-rolled recording stub.
        var recorder = new RecordingFaceExtractor();

        var result = new VoxelFloodFillDetector(faceExtractor: recorder).Detect(Model.Elements);

        var exteriorIds = result.Classifications.Where(c => c.IsExterior).Select(c => c.Element.GlobalId).ToHashSet(StringComparer.Ordinal);

        recorder.CalledFor.Should().BeEquivalentTo(exteriorIds);
    }

    [Fact]
    public void Detect_OnRealModel_ClassifiesAtLeastOneElementAsExterior()
    {
        // Smoke test that flood-fill reaches the model shell.
        var result = new VoxelFloodFillDetector().Detect(Model.Elements);

        result.Classifications.Any(c => c.IsExterior).Should().BeTrue();
    }

    [Fact]
    public void Detect_DifferentVoxelSize_DoesNotThrow_AndStillProducesClassifications()
    {
        // Larger voxel (1.0 m) is the safe direction — smaller multiplies cost ~8×.
        var result = new VoxelFloodFillDetector(1.0).Detect(Model.Elements);

        result.Classifications.Count.Should().Be(Model.Elements.Count);
        result.Classifications.Any(c => c.IsExterior).Should().BeTrue();
    }

    /// <summary>
    /// Records which element GlobalIds the strategy invokes extraction for, while delegating to the real <see cref="PcaFaceExtractor"/> so the rest
    /// of the pipeline observes a populated envelope.
    /// </summary>
    private sealed class RecordingFaceExtractor : IFaceExtractor
    {
        private readonly IFaceExtractor _inner = new PcaFaceExtractor();
        public HashSet<string> CalledFor { get; } = new(StringComparer.Ordinal);

        public IReadOnlyList<Face> Extract(IElement element)
        {
            CalledFor.Add(element.GlobalId);

            return _inner.Extract(element);
        }
    }
}
