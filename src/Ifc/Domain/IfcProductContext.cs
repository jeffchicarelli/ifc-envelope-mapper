using Xbim.Ifc4.Interfaces;

namespace IfcEnvelopeMapper.Ifc.Domain;

/// <summary>
/// Bundles an IFC product with its spatial parents.
/// </summary>
public readonly record struct IfcProductContext(
    IIfcProduct Product,
    IIfcBuilding? Building = null,
    IIfcBuildingStorey? Storey = null,
    IIfcSite? Site = null);
