using IfcEnvelopeMapper.Core.Domain.Element;
using IfcEnvelopeMapper.Core.Domain.Surface;

namespace IfcEnvelopeMapper.Core.Pipeline.Detection;

/// <summary>
/// Breaks a <see cref="BuildingElement"/>'s mesh into near-planar <see cref="Face"/>
/// regions. Strategies typically call this once per element to obtain per-face
/// plane and orientation before deciding exterior vs. interior.
/// </summary>
public interface IFaceExtractor
{
    IReadOnlyList<Face> Extract(BuildingElement element);
}
