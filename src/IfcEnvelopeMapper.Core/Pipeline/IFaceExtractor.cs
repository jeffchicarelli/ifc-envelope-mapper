using IfcEnvelopeMapper.Core.Building;
using IfcEnvelopeMapper.Core.Geometry;

namespace IfcEnvelopeMapper.Core.Pipeline;

public interface IFaceExtractor
{
    IReadOnlyList<Face> Extract(BuildingElement element);
}
