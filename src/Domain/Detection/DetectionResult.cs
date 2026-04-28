using IfcEnvelopeMapper.Domain.Surface;

namespace IfcEnvelopeMapper.Domain.Detection;

/// <summary>
/// Output of <see cref="Services.IEnvelopeDetector.Detect"/>: the derived <see cref="Envelope"/> plus a per-element classification indicating
/// whether each element is exterior.
/// </summary>
public sealed class DetectionResult(Envelope envelope, IReadOnlyList<ElementClassification> classifications)
{
    /// <summary>Derived exterior skin of the model.</summary>
    public Envelope Envelope { get; } = envelope;

    /// <summary>One classification per input element, in strategy-processing order.</summary>
    public IReadOnlyList<ElementClassification> Classifications { get; } = classifications;
}
