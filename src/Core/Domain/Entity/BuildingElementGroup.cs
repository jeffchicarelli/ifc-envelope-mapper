using g4;

namespace IfcEnvelopeMapper.Core.Domain.Element;

/// <summary>
/// A composite IFC construct (e.g., <c>IfcCurtainWall</c>, <c>IfcRoof</c>) that bundles
/// several <see cref="BuildingElement"/>s that should be reasoned about together.
/// Identity is the group's own <c>GlobalId</c>.
/// </summary>
public sealed class BuildingElementGroup : IEquatable<BuildingElementGroup>
{
    public required string GlobalId { get; init; }
    public required string IfcType { get; init; }
    public BuildingElementContext Context { get; init; }

    /// <summary>
    /// Mesh attached directly to the group entity when the IFC author modelled one.
    /// Most real files leave this <c>null</c> and keep geometry only on the members.
    /// </summary>
    public DMesh3? OwnMesh { get; init; }

    /// <summary>Members belonging to this group. Always set; may be empty.</summary>
    public required IReadOnlyList<BuildingElement> Elements { get; init; }

    public bool Equals(BuildingElementGroup? other)
        => other is not null && GlobalId == other.GlobalId;

    public override bool Equals(object? obj) => Equals(obj as BuildingElementGroup);

    public override int GetHashCode() => GlobalId.GetHashCode();
}
