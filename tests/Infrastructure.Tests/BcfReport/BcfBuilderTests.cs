using g4;
using IfcEnvelopeMapper.Application.Reports;
using IfcEnvelopeMapper.Domain.Detection;
using IfcEnvelopeMapper.Domain.Surface;

namespace IfcEnvelopeMapper.Infrastructure.Tests.BcfReport;

[Trait("Category", "Integration")]
public sealed class BcfBuilderTests : IfcTestBase
{
    public BcfBuilderTests() : base("duplex.ifc") { }

    [Fact]
    public void Build_OneTopicPerExteriorElement()
    {
        var result = MakeResult(3, 2);

        var package = BcfBuilder.Build(result);

        package.Topics.Should().HaveCount(3);
    }

    [Fact]
    public void Build_NoExteriorElements_ProducesEmptyPackage()
    {
        var result = MakeResult(0, 5);

        var package = BcfBuilder.Build(result);

        package.Topics.Should().BeEmpty();
        package.Version.Should().Be(BcfBuilder.VERSION);
    }

    [Fact]
    public void Build_TopicsAreSortedByGlobalIdOrdinal()
    {
        var result = MakeResult(5, 0);

        var package = BcfBuilder.Build(result);

        var titleIds = package.Topics.Select(t => ExtractGuidFromTitle(t.Title)).ToList();

        var sortedIds = titleIds.OrderBy(s => s, StringComparer.Ordinal).ToList();

        titleIds.Should().Equal(sortedIds);
    }

    [Fact]
    public void Build_ViewpointReferencesElementIfcGuid()
    {
        var element = Model.Elements[0];

        var result = new DetectionResult(new Envelope(new DMesh3(), Array.Empty<Face>()),
                                         new[] { new ElementClassification(element, true, Array.Empty<Face>()) });

        var package = BcfBuilder.Build(result);

        var topic = package.Topics.Single();
        topic.Viewpoint.IfcGuid.Should().Be(element.GlobalId);
    }

    [Fact]
    public void Build_TopicHasConstantTypeStatusAuthor()
    {
        var result = MakeResult(1, 0);

        var topic = BcfBuilder.Build(result).Topics.Single();

        topic.TopicType.Should().Be(BcfBuilder.TOPIC_TYPE);
        topic.TopicStatus.Should().Be(BcfBuilder.TOPIC_STATUS);
        topic.CreationAuthor.Should().Be(BcfBuilder.CREATION_AUTHOR);
    }

    [Fact]
    public void Build_TopicTitle_IncludesIfcTypeAndGlobalId()
    {
        var element = Model.Elements[0];

        var result = new DetectionResult(new Envelope(new DMesh3(), Array.Empty<Face>()),
                                         new[] { new ElementClassification(element, true, Array.Empty<Face>()) });

        var topic = BcfBuilder.Build(result).Topics.Single();

        topic.Title.Should().Contain(element.IfcType);
        topic.Title.Should().Contain(element.GlobalId);
    }

    [Fact]
    public void Build_CameraPosition_IsAwayFromElementCentre()
    {
        var element = Model.Elements[0];

        var result = new DetectionResult(new Envelope(new DMesh3(), Array.Empty<Face>()),
                                         new[] { new ElementClassification(element, true, Array.Empty<Face>()) });

        var topic = BcfBuilder.Build(result).Topics.Single();
        var bbox = element.GetMesh().GetBounds();

        topic.Viewpoint.Camera.Position.Distance(bbox.Center).Should().BeGreaterThan(2.0);
        topic.Viewpoint.Camera.Direction.Length.Should().BeApproximately(1.0, 1e-9);
        topic.Viewpoint.Camera.UpVector.Should().Be(new Vector3d(0, 0, 1));
    }

    [Fact]
    public void Build_TopicGuids_AreUnique()
    {
        var result = MakeResult(5, 0);

        var topics = BcfBuilder.Build(result).Topics;

        topics.Select(t => t.TopicGuid).Distinct().Count().Should().Be(topics.Count);
        topics.Select(t => t.Viewpoint.ViewpointGuid).Distinct().Count().Should().Be(topics.Count);
    }

    private DetectionResult MakeResult(int exteriorCount, int interiorCount)
    {
        var elements = Model.Elements.Take(exteriorCount + interiorCount).ToList();

        var classifications = elements.Select((e, i) => new ElementClassification(e, i < exteriorCount, Array.Empty<Face>())).ToList();

        return new DetectionResult(new Envelope(new DMesh3(), Array.Empty<Face>()), classifications);
    }

    private static string ExtractGuidFromTitle(string title)
    {
        // Title format: "Exterior: <IfcType> <GlobalId>". GlobalId is the last token.
        var parts = title.Split(' ');

        return parts[^1];
    }
}
