using IfcEnvelopeMapper.Core.Element;

namespace IfcEnvelopeMapper.Core.Loading;

public sealed record ModelLoadResult(
    IReadOnlyList<BuildingElement> Elements,
    IReadOnlyList<BuildingElementGroup> Groups);
