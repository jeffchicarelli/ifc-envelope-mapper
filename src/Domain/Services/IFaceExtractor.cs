using IfcEnvelopeMapper.Domain.Interfaces;
using IfcEnvelopeMapper.Domain.Surface;

namespace IfcEnvelopeMapper.Domain.Services;

/// <summary>
/// Breaks an <see cref="IElement"/>'s mesh into near-planar <see cref="Face"/> regions. Strategies typically call this once per element to
/// obtain per-face plane and orientation before deciding exterior vs. interior.
/// </summary>
public interface IFaceExtractor
{
    IReadOnlyList<Face> Extract(IElement element);
}
