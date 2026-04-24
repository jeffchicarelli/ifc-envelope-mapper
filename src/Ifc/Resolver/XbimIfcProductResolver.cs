using Xbim.Ifc;
using Xbim.Ifc4.Interfaces;

namespace IfcEnvelopeMapper.Ifc.Resolver;

public sealed class XbimIfcProductResolver : IDisposable
{
    private readonly IfcStore _store;
    private readonly Dictionary<string, IIfcProduct> _index;

    public XbimIfcProductResolver(string path)
    {
        _store = IfcStore.Open(path);

        // IFC spec requires unique GlobalId; ToDictionary throws on duplicates (fail-loud on broken models).
        _index = _store.Instances.OfType<IIfcProduct>()
                       .ToDictionary(p => p.GlobalId.ToString(), StringComparer.Ordinal);
    }

    public IIfcProduct? Resolve(string globalId)
    {
        return _index.GetValueOrDefault(globalId);
    }

    public void Dispose() => _store.Dispose();
}
