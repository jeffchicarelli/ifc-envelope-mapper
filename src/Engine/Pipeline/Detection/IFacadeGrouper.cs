using IfcEnvelopeMapper.Ifc.Domain.Surface;

namespace IfcEnvelopeMapper.Engine.Pipeline.Detection;

/// <summary>
/// Groups the exterior faces of an <see cref="Envelope"/> into oriented
/// <see cref="Facade"/> slices — Stage 2 of the detection pipeline.
/// Analogous to <see cref="IEnvelopeDetector"/> for Stage 1.
/// </summary>
public interface IFacadeGrouper
{
    IReadOnlyList<Facade> Group(Envelope envelope);
}
