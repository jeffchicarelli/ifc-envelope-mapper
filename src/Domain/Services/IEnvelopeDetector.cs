using IfcEnvelopeMapper.Domain.Detection;
using IfcEnvelopeMapper.Domain.Interfaces;

namespace IfcEnvelopeMapper.Domain.Services;

/// <summary>
/// Classifies each <see cref="IElement"/> as exterior or interior. Implementations differ in method (voxel flood-fill, ray casting, …); the
/// contract is the same — in, a set of elements; out, a <see cref="DetectionResult"/>.
/// </summary>
public interface IEnvelopeDetector
{
    /// <summary>The runtime parameters this instance was constructed with.</summary>
    StrategyConfig Config { get; }

    DetectionResult Detect(IEnumerable<IElement> elements);
}
