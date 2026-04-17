using g4;
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

        foreach (var element in elements)
        {
            buildingElements.Add(new BuildingElement(
                element.GlobalId,
                element.GetType().Name,
                new DMesh3()));
        }

        return buildingElements;
    }
}
