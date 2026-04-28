using IfcEnvelopeMapper.Domain.Interfaces;

namespace IfcEnvelopeMapper.Application.Ports;

/// <summary>
/// Output of loading an IFC file. Owns an opaque <see cref="IDisposable"/> lifetime (the underlying xBIM store) so element lazy-accessors
/// remain valid as long as this result is alive. Callers must dispose after detection is complete.
/// </summary>
public sealed class ModelLoadResult : IDisposable
{
    private readonly IDisposable _lifetime;

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

    /// <summary>Individual constructive elements loaded from the IFC file, ready for detection.</summary>
    public IReadOnlyList<IElement> Elements { get; }

    /// <summary>Aggregate products (e.g. IfcCurtainWall) whose children are included in <see cref="Elements"/>.</summary>
    public IReadOnlyList<IElement> Composites { get; }

    /// <summary>Absolute path to the source IFC file.</summary>
    public string FilePath { get; }

    /// <summary>IFC schema identifier read from the file header (e.g. <c>"IFC4"</c>).</summary>
    public string SchemaVersion { get; }

    /// <summary>Releases the underlying xBIM store, invalidating all lazy element accessors.</summary>
    public void Dispose()
    {
        _lifetime.Dispose();
    }
}
