using IfcEnvelopeMapper.Core.Building;

namespace IfcEnvelopeMapper.Core.Pipeline;

public sealed record ModelLoadResult(
    IReadOnlyList<BuildingElement> Elements,
    IReadOnlyList<BuildingElementGroup> Groups);
