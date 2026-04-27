using IfcEnvelopeMapper.Ifc.Domain;
using IfcEnvelopeMapper.Ifc.Loading;
using Xbim.Ifc4.Interfaces;

namespace IfcEnvelopeMapper.Tests;

/// <summary>
/// Base class for tests that need a loaded IFC model. Caches the loaded
/// <see cref="ModelLoadResult"/> per (test-class type, IFC file path) so
/// xUnit's per-test-method instance lifecycle does not re-load the same
/// IFC. Cache lives in a static field but is keyed on the derived test
/// class type, so different test classes get isolated entries even for
/// the same file. Also exposes filesystem helpers that walk parents to
/// find the repo's <c>data/</c> folder.
/// </summary>
/// <remarks>
/// Three usage patterns:
/// <list type="number">
/// <item>
/// <description>Single default IFC for the whole test class — pass the file
/// name to the base constructor; access <see cref="Model"/> /
/// <see cref="IfcPath"/> directly.</description>
/// </item>
/// <item>
/// <description>Multi-IFC test methods — leave the constructor parameterless
/// and call <see cref="LoadModel"/> per test.</description>
/// </item>
/// <item>
/// <description>Theory-driven — leave the constructor parameterless and call
/// <see cref="LoadDefault"/> at the start of each test to set the active
/// model.</description>
/// </item>
/// </list>
/// The cache is never disposed; it lives for the test process lifetime.
/// In an ephemeral xUnit process this is fine. Each entry holds an open
/// <see cref="Xbim.Ifc.IfcStore"/>.
/// </remarks>
public abstract class IfcTestBase
{
    private static readonly Dictionary<(Type, string), ModelLoadResult> _cache = new();
    private static readonly object _cacheLock = new();

    /// <summary>Path of the currently associated IFC file.</summary>
    protected string IfcPath { get; private set; } = null!;

    /// <summary>The model currently associated with this test instance.</summary>
    protected ModelLoadResult Model { get; private set; } = null!;

    /// <summary>No default model. Tests call <see cref="LoadModel"/> or <see cref="LoadDefault"/> per test.</summary>
    protected IfcTestBase() { }

    /// <summary>Associates a default IFC with this test class.</summary>
    protected IfcTestBase(string ifcFileName)
    {
        LoadDefault(ifcFileName);
    }

    /// <summary>Replaces the active model. Useful in [Theory] tests.</summary>
    protected void LoadDefault(string ifcFileName)
    {
        IfcPath = FindModel(ifcFileName);
        Model = LoadCached(IfcPath);
    }

    /// <summary>Loads (or retrieves from cache) without changing the active model.</summary>
    protected ModelLoadResult LoadModel(string ifcFileName)
    {
        return LoadCached(FindModel(ifcFileName));
    }

    /// <summary>Finds an element of the active model by GlobalId.</summary>
    /// <exception cref="InvalidOperationException">No element with that GlobalId.</exception>
    protected Element FindElement(string globalId)
    {
        return Model.Elements.First(e => e.GlobalId == globalId);
    }

    /// <summary>Filters elements of the active model by IFC product type.</summary>
    protected IEnumerable<Element> ElementsOfType<T>() where T : IIfcProduct
    {
        return Model.Elements.Where(e => e.GetIfcProduct() is T);
    }

    /// <summary>Resolves an IFC file under <c>data/models/</c> by walking parents.</summary>
    protected static string FindModel(string fileName)
        => FindUpward(Path.Combine("data", "models", fileName))
           ?? throw new FileNotFoundException(
               $"{fileName} not found in any parent of " + Directory.GetCurrentDirectory());

    /// <summary>Resolves a ground-truth CSV path. Returns the canonical write
    /// location next to <c>data/ground-truth/</c> if the file does not yet exist
    /// (so generators can create it).</summary>
    protected static string GroundTruthPath(string fileName)
    {
        var found = FindUpward(Path.Combine("data", "ground-truth", fileName));
        if (found is not null) return found;

        var dataDir = FindUpward("data") ?? throw new DirectoryNotFoundException(
            "data directory not found upward from " + Directory.GetCurrentDirectory());
        return Path.Combine(dataDir, "ground-truth", fileName);
    }

    /// <summary>Resolves a results-artefact path under <c>data/results/</c>.
    /// The folder may not exist yet — caller creates it.</summary>
    protected static string ResultsPath(string fileName)
    {
        var dataDir = FindUpward("data") ?? throw new DirectoryNotFoundException(
            "data directory not found upward from " + Directory.GetCurrentDirectory());
        return Path.Combine(dataDir, "results", fileName);
    }

    private static string? FindUpward(string relative)
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate) || Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return null;
    }

    private ModelLoadResult LoadCached(string path)
    {
        var key = (GetType(), path);
        lock (_cacheLock)
        {
            if (!_cache.TryGetValue(key, out var model))
            {
                model = new XbimModelLoader().Load(path);
                _cache[key] = model;
            }

            return model;
        }
    }
}
