using IfcEnvelopeMapper.Ifc.Domain;
using IfcEnvelopeMapper.Ifc.Domain.Surface;

namespace IfcEnvelopeMapper.Engine.Pipeline.Detection;

/// <summary>
/// Breaks a <see cref="Element"/>'s mesh into near-planar <see cref="Face"/>
/// regions. Strategies typically call this once per element to obtain per-face
/// plane and orientation before deciding exterior vs. interior.
/// </summary>
public interface IFaceExtractor
{
    IReadOnlyList<Face> Extract(Element element);
}
