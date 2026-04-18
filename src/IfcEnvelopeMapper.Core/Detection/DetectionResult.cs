using IfcEnvelopeMapper.Core.Surface;

namespace IfcEnvelopeMapper.Core.Detection;

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
