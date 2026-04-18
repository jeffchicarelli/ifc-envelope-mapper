namespace IfcEnvelopeMapper.Core.Element;

public readonly record struct BuildingElementContext(
    string? SiteId = null,
    string? BuildingId = null,
    string? StoreyId = null);
