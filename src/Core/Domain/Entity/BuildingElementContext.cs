namespace IfcEnvelopeMapper.Core.Domain.Element;

/// <summary>
/// The IFC spatial containment an element belongs to: <c>IfcSite → IfcBuilding → IfcBuildingStorey</c>.
/// All fields are optional because small synthetic IFCs often omit parts of the hierarchy.
/// </summary>
public readonly record struct BuildingElementContext(
    string? SiteId = null,
    string? BuildingId = null,
    string? StoreyId = null);
