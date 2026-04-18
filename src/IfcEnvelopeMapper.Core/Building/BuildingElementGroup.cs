using g4;

namespace IfcEnvelopeMapper.Core.Building;

public sealed class BuildingElementGroup : IEquatable<BuildingElementGroup>
{
    public required string GlobalId { get; init; }
    public required string IfcType { get; init; }
    public BuildingElementContext Context { get; init; }
    public DMesh3? OwnMesh { get; init; }
    public required IReadOnlyList<BuildingElement> Elements { get; init; }

    public bool Equals(BuildingElementGroup? other)
        => other is not null && GlobalId == other.GlobalId;

    public override bool Equals(object? obj) => Equals(obj as BuildingElementGroup);

    public override int GetHashCode() => GlobalId.GetHashCode();
}
