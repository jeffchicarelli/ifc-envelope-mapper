using IfcEnvelopeMapper.Domain.Interfaces;
using Xbim.Ifc4.Interfaces;

namespace IfcEnvelopeMapper.Infrastructure.Ifc;

/// <summary>
/// Domain entity wrapping an <see cref="IIfcBuildingStorey"/>. Carries identity
/// and the storey elevation; no geometry. Equality is identity by
/// <see cref="GlobalId"/>.
/// </summary>
public class Storey :
    IIfcEntity,
    IEquatable<Storey>
{
    protected readonly IIfcBuildingStorey _storey;

    /// <inheritdoc/>
    public string GlobalId => _storey.GlobalId;
    /// <inheritdoc/>
    public string? Name    => _storey.Name;
    /// <summary>Storey elevation above site datum in world units. Returns 0.0 when the IFC attribute is absent.</summary>
    public double Elevation => _storey.Elevation ?? 0.0;

    /// <summary>Returns the underlying xBIM storey handle.</summary>
    public IIfcBuildingStorey GetIfcStorey() => _storey;

    public bool Equals(Storey? other) =>
        other is not null && GlobalId == other.GlobalId;

    public override bool Equals(object? obj) => Equals(obj as Storey);

    public override int GetHashCode() => GlobalId.GetHashCode();

    public Storey(IIfcBuildingStorey storey)
    {
        _storey = storey;
    }
}
