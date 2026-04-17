using IfcEnvelopeMapper.Core.Building;
using IfcEnvelopeMapper.Core.Pipeline;
using Xbim.Ifc;
using Xbim.Ifc4.Interfaces;

namespace IfcEnvelopeMapper.Ifc;

public sealed class XbimModelLoader : IModelLoader
{
    public IReadOnlyList<BuildingElement> Load(string path)
    {
        using var model = IfcStore.Open(path);
        var elements = model.Instances.OfType<IIfcBuildingElement>();

        var buildingElements = new List<BuildingElement>();

        foreach (var ifcBuildingElement in elements)
        {
            buildingElements.Add(new BuildingElement(ifcBuildingElement.GlobalId, ifcBuildingElement.GetType().Name));
        }

        return buildingElements;
    }
}
