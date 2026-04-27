using g4;

namespace IfcEnvelopeMapper.Core.Domain.Element;

/// <summary>
/// A single IFC element carried through the pipeline — the thing we ultimately
/// classify as exterior or interior. Identity is the IFC <c>GlobalId</c>
/// (a 22-character GUID), so two instances with the same id compare equal.
/// </summary>
public sealed class BuildingElement : IEquatable<BuildingElement>
{
    public required string GlobalId { get; init; }
    public required string IfcType { get; init; }

    /// <summary>Triangulated geometry in world coordinates, already baked from the IFC placement.</summary>
    public required DMesh3 Mesh { get; init; }

    public BuildingElementContext Context { get; init; }

    /// <summary>
    /// <c>GlobalId</c> of the parent <see cref="BuildingElementGroup"/>, or <c>null</c> if standalone.
    /// Used to keep grouped elements (e.g., a curtain-wall system) together during analysis.
    /// </summary>
    public string? GroupGlobalId { get; init; }

    public bool Equals(BuildingElement? other)
        => other is not null && GlobalId == other.GlobalId;

    public override bool Equals(object? obj) => Equals(obj as BuildingElement);

    public override int GetHashCode() => GlobalId.GetHashCode();
}
