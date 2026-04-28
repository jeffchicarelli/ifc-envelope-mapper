namespace IfcEnvelopeMapper.Domain.Interfaces;

/// <summary>
/// A buildng element with identity, geometry, and a bounding box. The domain-facing counterpart to the xBIM-coupled <c>Element</c>
/// infrastructure type — all domain services and value objects operate on this interface so the domain assembly stays free of xBIM dependencies.
/// </summary>
public interface IElement : IBoxEntity, IIfcEntity, IMeshEntity
{
    /// <summary>IFC schema class name (e.g. "IfcWall", "IfcSlab").</summary>
    string IfcType { get; }
}
