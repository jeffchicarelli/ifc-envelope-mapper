using IfcEnvelopeMapper.Core.Domain.Element;
using IfcEnvelopeMapper.Core.Domain.Surface;

namespace IfcEnvelopeMapper.Core.Pipeline.Detection;

public interface IFaceExtractor
{
    IReadOnlyList<Face> Extract(BuildingElement element);
}
