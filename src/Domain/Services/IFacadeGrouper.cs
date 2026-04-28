using IfcEnvelopeMapper.Domain.Surface;

namespace IfcEnvelopeMapper.Domain.Services;

/// <summary>
/// Groups the exterior faces of an <see cref="Envelope"/> into oriented
/// <see cref="Facade"/> slices — Stage 2 of the detection pipeline.
/// Analogous to <see cref="IEnvelopeDetector"/> for Stage 1.
/// </summary>
public interface IFacadeGrouper
{
    IReadOnlyList<Facade> Group(Envelope envelope);
}
