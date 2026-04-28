namespace IfcEnvelopeMapper.Domain.Interfaces;

/// <summary>Identifies and labels an entity by its STEP GUID and the optional name supplied by the IFC author.</summary>
public interface IIfcEntity
{
    /// <summary>22-character IFC STEP GlobalId, unique within the file.</summary>
    string GlobalId { get; }

    /// <summary>Optional human-readable label assigned by the IFC author.</summary>
    string? Name { get; }
}
