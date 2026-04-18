using IfcEnvelopeMapper.Core.Element;
using IfcEnvelopeMapper.Core.Surface;

namespace IfcEnvelopeMapper.Core.Detection;

public interface IFaceExtractor
{
    IReadOnlyList<Face> Extract(BuildingElement element);
}
