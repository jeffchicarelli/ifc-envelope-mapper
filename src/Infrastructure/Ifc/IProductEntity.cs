using Xbim.Ifc4.Interfaces;

namespace IfcEnvelopeMapper.Infrastructure.Ifc;

/// <summary>
/// Exposes the underlying IFC product of an entity together with its spatial
/// parents (Site, Building, Storey).
/// </summary>
public interface IProductEntity
{
    /// <summary>Returns the raw xBIM product handle.</summary>
    IIfcProduct GetIfcProduct();
    /// <summary>Returns the site spatial parent, or <c>null</c> if not present in the hierarchy.</summary>
    IIfcSite? GetIfcSite();
    /// <summary>Returns the building spatial parent, or <c>null</c> if not present in the hierarchy.</summary>
    IIfcBuilding? GetIfcBuilding();
    /// <summary>Returns the storey spatial parent, or <c>null</c> if not present in the hierarchy.</summary>
    IIfcBuildingStorey? GetIfcStorey();
}
