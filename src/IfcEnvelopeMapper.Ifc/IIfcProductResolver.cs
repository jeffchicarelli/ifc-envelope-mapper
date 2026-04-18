using Xbim.Ifc4.Interfaces;

namespace IfcEnvelopeMapper.Ifc;

public interface IIfcProductResolver
{
    IIfcProduct? Resolve(string globalId);
}
