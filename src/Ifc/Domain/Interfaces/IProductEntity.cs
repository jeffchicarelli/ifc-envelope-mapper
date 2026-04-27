using Xbim.Ifc4.Interfaces;

namespace IfcEnvelopeMapper.Ifc.Domain.Interfaces;

/// <summary>
/// Exposes the underlying IFC product of an entity together with its spatial
/// parents (Site, Building, Storey).
/// </summary>
public interface IProductEntity
{
    IIfcProduct GetIfcProduct();
    IIfcSite? GetIfcSite();
    IIfcBuilding? GetIfcBuilding();
    IIfcBuildingStorey? GetIfcStorey();
}
