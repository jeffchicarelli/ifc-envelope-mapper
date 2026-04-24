using g4;

namespace IfcEnvelopeMapper.Core.Domain.Element;

public sealed class BuildingElement : IEquatable<BuildingElement>
{
    public required string GlobalId { get; init; }
    public required string IfcType { get; init; }
    public required DMesh3 Mesh { get; init; }
    public BuildingElementContext Context { get; init; }
    public string? GroupGlobalId { get; init; }

    public bool Equals(BuildingElement? other)
        => other is not null && GlobalId == other.GlobalId;

    public override bool Equals(object? obj) => Equals(obj as BuildingElement);

    public override int GetHashCode() => GlobalId.GetHashCode();
}
