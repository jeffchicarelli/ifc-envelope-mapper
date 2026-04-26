using g4;
using IfcEnvelopeMapper.Core.Domain.Element;
using IfcEnvelopeMapper.Core.Pipeline.Detection;

namespace IfcEnvelopeMapper.Core.Pipeline.Bcf;

/// <summary>
/// Builds a <see cref="BcfReport"/> from a <see cref="DetectionResult"/>
/// by emitting one <see cref="BcfTopic"/> per exterior element. Pure: no
/// I/O. The actual zip + XML emission is <see cref="BcfWriter"/>.
/// </summary>
public static class BcfBuilder
{
    public const string VERSION         = "2.1";
    public const string TOPIC_TYPE      = "Exterior Envelope";
    public const string TOPIC_STATUS    = "Open";
    public const string CREATION_AUTHOR = "ifcenvmapper";

    private const double FIELD_OF_VIEW_DEG = 60.0;
    private const double MIN_STANDOFF_M    = 5.0;
    private const double STANDOFF_FACTOR   = 2.0;

    public static BcfReport Build(DetectionResult result)
    {
        var topics = result.Classifications
            .Where(c => c.IsExterior)
            .OrderBy(c => c.Element.GlobalId, StringComparer.Ordinal)
            .Select(c => BuildTopic(c.Element))
            .ToList();

        return new BcfReport(VERSION, topics);
    }

    private static BcfTopic BuildTopic(BuildingElement element)
    {
        var bbox     = element.Mesh.GetBounds();
        var centre   = bbox.Center;
        var diagonal = bbox.Diagonal.Length;
        var standoff = Math.Max(STANDOFF_FACTOR * diagonal, MIN_STANDOFF_M);

        var position  = new Vector3d(centre.x, centre.y - standoff, centre.z + 0.3 * standoff);
        var direction = (centre - position).Normalized;

        var camera = new BcfCamera(
            Position:    position,
            Direction:   direction,
            UpVector:    new Vector3d(0, 0, 1),
            FieldOfView: FIELD_OF_VIEW_DEG);

        var viewpoint = new BcfViewpoint(
            ViewpointGuid: Guid.NewGuid(),
            IfcGuid:       element.GlobalId,
            Camera:        camera);

        return new BcfTopic(
            TopicGuid:      Guid.NewGuid(),
            Title:          $"Exterior: {element.IfcType} {element.GlobalId}",
            TopicType:      TOPIC_TYPE,
            TopicStatus:    TOPIC_STATUS,
            CreationDate:   DateTimeOffset.UtcNow,
            CreationAuthor: CREATION_AUTHOR,
            Viewpoint:      viewpoint);
    }
}
