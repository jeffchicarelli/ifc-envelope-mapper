using Xbim.Ifc;
using Xbim.Ifc4.Interfaces;

namespace IfcEnvelopeMapper.Infrastructure.Ifc.Resolver;

/// <summary>
/// Resolves IFC element GlobalIds to their <see cref="IIfcProduct"/> handles by building
/// an in-memory index over the entire file. Throws on duplicate GlobalIds (broken IFC).
/// </summary>
public sealed class XbimProductResolver : IDisposable
{
    private readonly IfcStore _store;
    private readonly Dictionary<string, IIfcProduct> _index;

    /// <summary>Opens <paramref name="path"/> and indexes all <c>IIfcProduct</c> entities by GlobalId.</summary>
    public XbimProductResolver(string path)
    {
        _store = IfcStore.Open(path);

        // IFC spec requires unique GlobalId; ToDictionary throws on duplicates (fail-loud on broken models).
        _index = _store.Instances.OfType<IIfcProduct>()
                       .ToDictionary(p => p.GlobalId.ToString(), StringComparer.Ordinal);
    }

    /// <summary>Returns the product with the given <paramref name="globalId"/>, or <c>null</c> if not found.</summary>
    public IIfcProduct? Resolve(string globalId)
    {
        return _index.GetValueOrDefault(globalId);
    }

    public void Dispose() => _store.Dispose();
}
