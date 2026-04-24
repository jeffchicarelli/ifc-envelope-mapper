using IfcEnvelopeMapper.Core.Domain.Element;

namespace IfcEnvelopeMapper.Core.Pipeline.Detection;

public interface IDetectionStrategy
{
    DetectionResult Detect(IEnumerable<BuildingElement> elements);
}
