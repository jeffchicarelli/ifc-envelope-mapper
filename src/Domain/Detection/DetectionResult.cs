using IfcEnvelopeMapper.Domain.Surface;

namespace IfcEnvelopeMapper.Domain.Detection;

/// <summary>
/// Output of <see cref="Services.IEnvelopeDetector.Detect"/>: the derived <see cref="Envelope"/>
/// plus a per-element classification indicating whether each element is exterior.
/// </summary>
public sealed class DetectionResult
{
    public Envelope Envelope { get; }
    public IReadOnlyList<ElementClassification> Classifications { get; }

    public DetectionResult(Envelope envelope, IReadOnlyList<ElementClassification> classifications)
    {
        Envelope = envelope;
        Classifications = classifications;
    }
}
