using g4;

namespace IfcEnvelopeMapper.Domain.Interfaces;

/// <summary>
/// Provides triangulated geometry as a <see cref="DMesh3"/>.
/// </summary>
public interface IMeshEntity
{
    /// <summary>Returns the triangulated geometry in world coordinates.</summary>
    DMesh3 GetMesh();
}
