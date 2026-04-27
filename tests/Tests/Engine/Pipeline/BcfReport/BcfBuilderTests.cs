using g4;
using IfcEnvelopeMapper.Engine.Pipeline.BcfReport;
using IfcEnvelopeMapper.Engine.Pipeline.Detection;
using IfcEnvelopeMapper.Ifc.Domain.Surface;

namespace IfcEnvelopeMapper.Tests.Engine.Pipeline.BcfReport;

/// <summary>
/// Unit tests for <see cref="BcfBuilder.Build"/>. Synthesises
/// <see cref="DetectionResult"/>s from real <c>duplex.ifc</c> elements and
/// asserts the structural invariants of the resulting <see cref="BcfPackage"/>:
/// one topic per exterior element, sorted by GlobalId, viewpoint references
/// the IfcGuid, and constant fields populated.
/// </summary>
[Trait("Category", "Integration")]
public sealed class BcfBuilderTests : IfcTestBase
{
    public BcfBuilderTests() : base("duplex.ifc") { }

    [Fact]
    public void Build_OneTopicPerExteriorElement()
    {
        // 3 exterior + 2 interior → 3 topics.
        var result = MakeResult(exteriorCount: 3, interiorCount: 2);

        var package = BcfBuilder.Build(result);

        package.Topics.Should().HaveCount(3);
    }

    [Fact]
    public void Build_NoExteriorElements_ProducesEmptyPackage()
    {
        var result = MakeResult(exteriorCount: 0, interiorCount: 5);

        var package = BcfBuilder.Build(result);

        package.Topics.Should().BeEmpty();
        package.Version.Should().Be(BcfBuilder.VERSION);
    }

    [Fact]
    public void Build_TopicsAreSortedByGlobalIdOrdinal()
    {
        // Determinism: two runs must produce identically ordered topics so
        // the resulting .bcf zip is reproducible.
        var result = MakeResult(exteriorCount: 5, interiorCount: 0);

        var package = BcfBuilder.Build(result);

        var titleIds   = package.Topics.Select(t => ExtractGuidFromTitle(t.Title)).ToList();
        var sortedIds  = titleIds.OrderBy(s => s, StringComparer.Ordinal).ToList();
        titleIds.Should().Equal(sortedIds);
    }

    [Fact]
    public void Build_ViewpointReferencesElementIfcGuid()
    {
        // The receiving authoring tool uses Components/Selection/Component@IfcGuid
        // to highlight the offending element. Mismatch here = silently broken BCF.
        var element = Model.Elements[0];
        var result  = new DetectionResult(
            envelope:        new Envelope(new DMesh3(), Array.Empty<Face>()),
            classifications: new[] { new ElementClassification(element, isExterior: true, Array.Empty<Face>()) });

        var package = BcfBuilder.Build(result);

        var topic = package.Topics.Single();
        topic.Viewpoint.IfcGuid.Should().Be(element.GlobalId);
    }

    [Fact]
    public void Build_TopicHasConstantTypeStatusAuthor()
    {
        // The constants are pinned by the source — guards against a silent
        // change to "Closed" or an empty author string.
        var result = MakeResult(exteriorCount: 1, interiorCount: 0);

        var topic = BcfBuilder.Build(result).Topics.Single();

        topic.TopicType.Should().Be(BcfBuilder.TOPIC_TYPE);
        topic.TopicStatus.Should().Be(BcfBuilder.TOPIC_STATUS);
        topic.CreationAuthor.Should().Be(BcfBuilder.CREATION_AUTHOR);
    }

    [Fact]
    public void Build_TopicTitle_IncludesIfcTypeAndGlobalId()
    {
        var element = Model.Elements[0];
        var result  = new DetectionResult(
            envelope:        new Envelope(new DMesh3(), Array.Empty<Face>()),
            classifications: new[] { new ElementClassification(element, isExterior: true, Array.Empty<Face>()) });

        var topic = BcfBuilder.Build(result).Topics.Single();

        topic.Title.Should().Contain(element.IfcType);
        topic.Title.Should().Contain(element.GlobalId);
    }

    [Fact]
    public void Build_CameraPosition_IsAwayFromElementCentre()
    {
        // The standoff (≥ 5 m, scaled by element diagonal) keeps the camera
        // outside the element so the viewer doesn't open inside the geometry.
        // Direction is normalised — required by BCF 2.1.
        var element = Model.Elements[0];
        var result  = new DetectionResult(
            envelope:        new Envelope(new DMesh3(), Array.Empty<Face>()),
            classifications: new[] { new ElementClassification(element, isExterior: true, Array.Empty<Face>()) });

        var topic = BcfBuilder.Build(result).Topics.Single();
        var bbox  = element.GetMesh().GetBounds();

        topic.Viewpoint.Camera.Position.Distance(bbox.Center).Should().BeGreaterThan(2.0);
        topic.Viewpoint.Camera.Direction.Length.Should().BeApproximately(1.0, 1e-9);
        topic.Viewpoint.Camera.UpVector.Should().Be(new Vector3d(0, 0, 1));
    }

    [Fact]
    public void Build_TopicGuids_AreUnique()
    {
        var result = MakeResult(exteriorCount: 5, interiorCount: 0);

        var topics = BcfBuilder.Build(result).Topics;

        topics.Select(t => t.TopicGuid).Distinct().Count().Should().Be(topics.Count);
        topics.Select(t => t.Viewpoint.ViewpointGuid).Distinct().Count().Should().Be(topics.Count);
    }

    private DetectionResult MakeResult(int exteriorCount, int interiorCount)
    {
        var elements = Model.Elements.Take(exteriorCount + interiorCount).ToList();
        var classifications = elements
            .Select((e, i) => new ElementClassification(e, isExterior: i < exteriorCount, Array.Empty<Face>()))
            .ToList();

        return new DetectionResult(
            envelope:        new Envelope(new DMesh3(), Array.Empty<Face>()),
            classifications: classifications);
    }

    private static string ExtractGuidFromTitle(string title)
    {
        // Title format: "Exterior: <IfcType> <GlobalId>". The GlobalId is the
        // last whitespace-separated token.
        var parts = title.Split(' ');
        return parts[^1];
    }
}
