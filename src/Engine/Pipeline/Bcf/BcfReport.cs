using g4;

namespace IfcEnvelopeMapper.Engine.Pipeline.Bcf;

/// <summary>
/// Package-level record. One <see cref="BcfReport"/> serialises to one
/// <c>.bcf</c> ZIP file: the top-level <c>bcf.version</c> plus one folder
/// per topic (named with the topic's GUID) containing <c>markup.bcf</c>
/// and <c>viewpoint.bcfv</c>.
/// </summary>
public sealed record BcfReport(
    string                  Version,
    IReadOnlyList<BcfTopic> Topics);

/// <summary>
/// A single BCF "issue". One topic per exterior element: the element's
/// IFC GlobalId is referenced inside the viewpoint's Components selection
/// so authoring tools highlight that element on import.
/// </summary>
public sealed record BcfTopic(
    Guid           TopicGuid,
    string         Title,
    string         TopicType,
    string         TopicStatus,
    DateTimeOffset CreationDate,
    string         CreationAuthor,
    BcfViewpoint   Viewpoint);

/// <summary>
/// Camera plus element selection. <c>IfcGuid</c> is the
/// <see cref="IfcEnvelopeMapper.Core.Domain.Element.BuildingElement"/>
/// GlobalId the receiving tool should highlight when the topic is opened.
/// </summary>
public sealed record BcfViewpoint(
    Guid      ViewpointGuid,
    string    IfcGuid,
    BcfCamera Camera);

/// <summary>
/// Perspective camera in IFC convention (Z-up, metres). <c>FieldOfView</c>
/// is vertical FOV in degrees, matching BCF 2.1's
/// <c>PerspectiveCamera.FieldOfView</c>.
/// </summary>
public sealed record BcfCamera(
    Vector3d Position,
    Vector3d Direction,
    Vector3d UpVector,
    double   FieldOfView);
