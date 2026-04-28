using g4;
using IfcEnvelopeMapper.Domain.Detection;
using IfcEnvelopeMapper.Domain.Interfaces;

namespace IfcEnvelopeMapper.Application.Reports;

/// <summary>
/// Builds a <see cref="BcfPackage"/> from a <see cref="DetectionResult"/> by emitting one <see cref="BcfTopic"/> per exterior element. Pure: no
/// I/O. The actual zip + XML emission is handled by <c>IBcfWriter</c>.
/// </summary>
public static class BcfBuilder
{
    public const string VERSION = "2.1";
    public const string TOPIC_TYPE = "Exterior Envelope";
    public const string TOPIC_STATUS = "Open";
    public const string CREATION_AUTHOR = "ifcenvmapper";

    private const double FIELD_OF_VIEW_DEG = 60.0;
    private const double MIN_STANDOFF_M = 5.0;
    private const double STANDOFF_FACTOR = 2.0;
    private const double CAMERA_ELEVATION_FACTOR = 0.3;

    /// <summary>Builds a <see cref="BcfPackage"/> from the exterior elements in <paramref name="result"/>, one topic per element, sorted by GlobalId.</summary>
    public static BcfPackage Build(DetectionResult result)
    {
        var topics = result.Classifications
                           .Where(c => c.IsExterior)
                           .OrderBy(c => c.Element.GlobalId, StringComparer.Ordinal)
                           .Select(c => BuildTopic(c.Element)).ToList();

        return new BcfPackage(VERSION, topics);
    }

    private static BcfTopic BuildTopic(IElement element)
    {
        var bbox = element.GetMesh().GetBounds();
        var centre = bbox.Center;
        var diagonal = bbox.Diagonal.Length;

        var standoff = Math.Max(STANDOFF_FACTOR * diagonal, MIN_STANDOFF_M);

        var position = new Vector3d(centre.x, centre.y - standoff, centre.z + (CAMERA_ELEVATION_FACTOR * standoff));

        var direction = (centre - position).Normalized;

        var camera = new BcfCamera(position, direction, new Vector3d(0, 0, 1), FIELD_OF_VIEW_DEG);

        var viewpoint = new BcfViewpoint(Guid.NewGuid(), element.GlobalId, camera);

        return new BcfTopic(Guid.NewGuid(), $"Exterior: {element.IfcType} {element.GlobalId}", TOPIC_TYPE, TOPIC_STATUS, DateTimeOffset.UtcNow,
                            CREATION_AUTHOR, viewpoint);
    }
}
