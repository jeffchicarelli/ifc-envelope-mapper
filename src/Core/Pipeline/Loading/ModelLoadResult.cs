using IfcEnvelopeMapper.Core.Domain.Element;

namespace IfcEnvelopeMapper.Core.Pipeline.Loading;

public sealed record ModelLoadResult(
    IReadOnlyList<BuildingElement> Elements,
    IReadOnlyList<BuildingElementGroup> Groups);
