using IfcEnvelopeMapper.Application.Ports;
using IfcEnvelopeMapper.Domain.Interfaces;
using IfcEnvelopeMapper.Infrastructure.Diagnostics;
using IfcEnvelopeMapper.Infrastructure.Ifc;
using IfcEnvelopeMapper.Infrastructure.Ifc.Loading;
using IfcEnvelopeMapper.Infrastructure.Visualization.Api;
using Microsoft.Extensions.Logging;
using Xbim.Ifc4.Interfaces;

namespace IfcEnvelopeMapper.Infrastructure.Tests;

/// <summary>
/// Base class for Infrastructure tests that need a loaded IFC model. Caches the
/// loaded <see cref="ModelLoadResult"/> per (test-class type, IFC file path).
/// </summary>
public abstract class IfcTestBase
{
    private static readonly Dictionary<(Type, string), ModelLoadResult> _cache = new();
    private static readonly object _cacheLock = new();

    static IfcTestBase()
    {
        var loggerFactory = LoggerFactory.Create(b => b
            .AddConsole()
            .SetMinimumLevel(LogLevel.Warning)
            .AddFilter("IfcEnvelopeMapper", LogLevel.Information));
        AppLog.Configure(loggerFactory);

        var glbPath      = Environment.GetEnvironmentVariable("IFC_DEBUG_GLB")
                           ?? Path.Combine(Path.GetTempPath(), "ifc-debug-output.glb");
        var launchServer = Environment.GetEnvironmentVariable("IFC_DEBUG_VIEWER") == "true";
        GeometryDebug.Configure(glbPath, launchServer);
    }

    protected string IfcPath { get; private set; } = null!;
    protected ModelLoadResult Model { get; private set; } = null!;

    protected IfcTestBase() { }

    protected IfcTestBase(string ifcFileName)
    {
        LoadDefault(ifcFileName);
    }

    protected void LoadDefault(string ifcFileName)
    {
        IfcPath = FindModel(ifcFileName);
        Model = LoadCached(IfcPath);
    }

    protected ModelLoadResult LoadModel(string ifcFileName)
    {
        return LoadCached(FindModel(ifcFileName));
    }

    /// <summary>Finds an element of the active model by GlobalId.</summary>
    protected Element FindElement(string globalId)
    {
        return (Element)Model.Elements.First(e => e.GlobalId == globalId);
    }

    /// <summary>Filters elements of the active model by IFC product type.</summary>
    protected IEnumerable<IElement> ElementsOfType<T>() where T : IIfcProduct
    {
        return Model.Elements.Where(e => e is IProductEntity p && p.GetIfcProduct() is T);
    }

    protected static string FindModel(string fileName)
        => FindUpward(Path.Combine("data", "models", fileName))
           ?? throw new FileNotFoundException(
               $"{fileName} not found in any parent of " + Directory.GetCurrentDirectory());

    protected static string GroundTruthPath(string fileName)
    {
        var found = FindUpward(Path.Combine("data", "ground-truth", fileName));
        if (found is not null)
        {
            return found;
        }

        var dataDir = FindUpward("data") ?? throw new DirectoryNotFoundException(
            "data directory not found upward from " + Directory.GetCurrentDirectory());
        return Path.Combine(dataDir, "ground-truth", fileName);
    }

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
