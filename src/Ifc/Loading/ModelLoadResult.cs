using IfcEnvelopeMapper.Ifc.Domain;
using Xbim.Ifc;

namespace IfcEnvelopeMapper.Ifc.Loading;

/// <summary>
/// Output of loading an IFC file. Holds the flat list of atomic
/// <see cref="Element"/>s plus a separate list of composite Elements
/// (each with <see cref="Element.Children"/> populated). Owns the underlying
/// <see cref="IfcStore"/> for the lifetime of the result so lazy mesh and
/// bounding box accessors on Element resolve against the live geometry context.
/// </summary>
public sealed class ModelLoadResult : IDisposable
{
    private readonly IfcStore _store;

    public IReadOnlyList<Element> Elements { get; }
    public IReadOnlyList<Element> Composites { get; }

    internal ModelLoadResult(
        IfcStore store,
        IReadOnlyList<Element> elements,
        IReadOnlyList<Element> composites)
    {
        _store = store;
        Elements = elements;
        Composites = composites;
    }

    public void Dispose() => _store.Dispose();
}
