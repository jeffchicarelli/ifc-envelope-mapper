namespace IfcEnvelopeMapper.Domain.Interfaces;

/// <summary>
/// Identifies and labels an entity by its STEP GUID and the optional name
/// supplied by the IFC author.
/// </summary>
public interface IIfcEntity
{
    string GlobalId { get; }
    string? Name { get; }
}
