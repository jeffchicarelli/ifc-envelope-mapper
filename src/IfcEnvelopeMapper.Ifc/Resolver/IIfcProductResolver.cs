using Xbim.Ifc4.Interfaces;

namespace IfcEnvelopeMapper.Ifc.Resolver;

public interface IIfcProductResolver
{
    IIfcProduct? Resolve(string globalId);
}
