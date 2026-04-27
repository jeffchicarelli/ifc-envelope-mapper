using IfcEnvelopeMapper.Ifc.Domain;

namespace IfcEnvelopeMapper.Engine.Pipeline.Detection;

/// <summary>
/// Classifies each <see cref="Element"/> as exterior or interior.
/// Implementations differ in method (voxel flood-fill, ray casting, …); the
/// contract is the same — in, a set of elements; out, a <see cref="DetectionResult"/>.
/// </summary>
public interface IEnvelopeDetector
{
    DetectionResult Detect(IEnumerable<Element> elements);
}
