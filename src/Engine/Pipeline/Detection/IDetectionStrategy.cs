using IfcEnvelopeMapper.Core.Domain.Element;

namespace IfcEnvelopeMapper.Engine.Pipeline.Detection;

/// <summary>
/// Classifies each <see cref="BuildingElement"/> as exterior or interior.
/// Implementations differ in method (voxel flood-fill, ray casting, …); the
/// contract is the same — in, a set of elements; out, a <see cref="DetectionResult"/>.
/// </summary>
public interface IDetectionStrategy
{
    DetectionResult Detect(IEnumerable<BuildingElement> elements);
}
