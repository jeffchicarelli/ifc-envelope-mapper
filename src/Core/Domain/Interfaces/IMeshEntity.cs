using g4;

namespace IfcEnvelopeMapper.Core.Domain.Interfaces;

/// <summary>
/// Provides triangulated geometry as a <see cref="DMesh3"/>.
/// </summary>
public interface IMeshEntity
{
    DMesh3 GetMesh();
}
