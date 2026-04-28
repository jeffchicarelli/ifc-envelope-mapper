using IfcEnvelopeMapper.Domain.Interfaces;

namespace IfcEnvelopeMapper.Application.Ports;

/// <summary>
/// Output of loading an IFC file. Owns an opaque <see cref="IDisposable"/> lifetime
/// (the underlying xBIM store) so element lazy-accessors remain valid as long as this
/// result is alive. Callers must dispose after detection is complete.
/// </summary>
public sealed class ModelLoadResult : IDisposable
{
    private readonly IDisposable _lifetime;

    public IReadOnlyList<IElement> Elements { get; }
    public IReadOnlyList<IElement> Composites { get; }
    public string FilePath { get; }
    public string SchemaVersion { get; }

    public ModelLoadResult(
        IDisposable lifetime,
        IReadOnlyList<IElement> elements,
        IReadOnlyList<IElement> composites,
        string filePath,
        string schemaVersion)
    {
        _lifetime = lifetime;
        Elements = elements;
        Composites = composites;
        FilePath = filePath;
        SchemaVersion = schemaVersion;
    }

    public void Dispose() => _lifetime.Dispose();
}
