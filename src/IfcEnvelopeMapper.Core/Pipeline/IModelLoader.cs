using IfcEnvelopeMapper.Core.Building;

namespace IfcEnvelopeMapper.Core.Pipeline;

public interface IModelLoader
{
    IReadOnlyList<BuildingElement> Load(string path);
}
