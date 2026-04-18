using IfcEnvelopeMapper.Core.Element;

namespace IfcEnvelopeMapper.Core.Detection;

public interface IDetectionStrategy
{
    DetectionResult Detect(IReadOnlyList<BuildingElement> elements);
}
