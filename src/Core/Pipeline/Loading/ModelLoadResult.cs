using IfcEnvelopeMapper.Core.Domain.Element;

namespace IfcEnvelopeMapper.Core.Pipeline.Loading;

/// <summary>
/// Output of loading an IFC file: the flat list of <see cref="BuildingElement"/>s
/// plus the <see cref="BuildingElementGroup"/>s they belong to (if any).
/// An element may appear in <see cref="Elements"/> and also be listed inside a group.
/// </summary>
public sealed record ModelLoadResult(
    IReadOnlyList<BuildingElement> Elements,
    IReadOnlyList<BuildingElementGroup> Groups);
