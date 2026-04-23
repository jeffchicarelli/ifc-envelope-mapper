using System.Diagnostics;
using IfcEnvelopeMapper.Algorithms.Detection;
using IfcEnvelopeMapper.Core.Evaluation;
using IfcEnvelopeMapper.Core.Loading;
using IfcEnvelopeMapper.Debug;
using IfcEnvelopeMapper.Ifc.Loading;
using Microsoft.Extensions.Logging;
using Xbim.Common.Configuration;
using Xbim.Ifc4.Interfaces;

// Start the debug viewer server NOW (before IFC loading or anything that might
// throw), not lazily on the first GeometryDebug.* call deep inside Detect.
// Clear() triggers GeometryDebug's static ctor — which binds port 5173 — and
// writes an empty GLB so the viewer has something to serve on first poll.
// [Conditional("DEBUG")] strips this call at Release compile time, so Release
// CLI runs never touch the port.
GeometryDebug.Clear();

using var loggerFactory = LoggerFactory.Create(b =>
    b.AddConsole().SetMinimumLevel(LogLevel.Warning));

XbimServices.Current.ConfigureServices(s =>
    s.AddXbimToolkit(c => c.AddLoggerFactory(loggerFactory)));

var ifcPath = FindUpward("data/models/duplex.ifc")
              ?? throw new FileNotFoundException("duplex.ifc not found in any parent directory");

Console.WriteLine($"Opening: {ifcPath}");

IModelLoader loader = new XbimModelLoader();
var model = loader.Load(ifcPath);

var first = model.Elements.First(e => e.Mesh.TriangleCount > 0);
Console.WriteLine($"First with geometry: {first.IfcType} {first.GlobalId} " +
                  $"tris={first.Mesh.TriangleCount} " +
                  $"bbox=({first.Mesh.GetBounds().Min}) → ({first.Mesh.GetBounds().Max})");

Console.WriteLine($"Elements loaded: {model.Elements.Count}");

// Ground truth generation — runs once, skipped if CSV already exists
var gtPath = Path.Combine(
    Path.GetDirectoryName(Path.GetDirectoryName(ifcPath))!,
    "ground-truth", "duplex.csv");

if (!File.Exists(gtPath))
{
    Console.WriteLine("Generating ground truth from IsExternal psets...");
    var ifcTypeById = model.Elements.ToDictionary(
        e => e.GlobalId,
        e => e.IfcType,
        StringComparer.Ordinal);

    using var store = Xbim.Ifc.IfcStore.Open(ifcPath);
    var lines = new List<string> { "GlobalId,IsExterior,Note" };

    foreach (var entity in store.Instances.OfType<IIfcBuildingElement>())
    {
        var gid = entity.GlobalId.ToString();
        if (!ifcTypeById.TryGetValue(gid, out var ifcType)) continue;

        var props = entity.IsDefinedBy
                          .Where(e => e is not null)
                          .SelectMany(r =>
                               (r.RelatingPropertyDefinition as IIfcPropertySet)?.HasProperties
                               ?? Enumerable.Empty<IIfcProperty>())
                          .OfType<IIfcPropertySingleValue>()
                          .Where(p => p.Name == "IsExternal")
                          .Select(p => p.NominalValue?.Value)
                          .OfType<bool>()
                          .ToList();

        // Distinguish 'pset absent' (null) from 'pset present with false' (false).
        bool? isExt = props.Count > 0 ? props[0] : null;

        var value = isExt switch
        {
            true  => "true",
            false => "false",
            null  => "unknown",
        };
        var note = isExt.HasValue ? string.Empty : $"{ifcType} (auto)";

        lines.Add($"{gid},{value},{note}");
    }

    File.WriteAllLines(gtPath, lines);
    Console.WriteLine($"Ground truth: {gtPath} ({lines.Count - 1} records)");
}

Console.WriteLine("Running VoxelFloodFillStrategy (voxelSize=0.5)...");
var sw = Stopwatch.StartNew();
var result = new VoxelFloodFillStrategy(voxelSize: 0.25).Detect(model.Elements);
sw.Stop();

var exterior = result.Classifications.Count(c => c.IsExterior);
var interior = result.Classifications.Count(c => !c.IsExterior);

Console.WriteLine($"Done in {sw.Elapsed.TotalSeconds:F2}s");
Console.WriteLine($"  Exterior : {exterior}");
Console.WriteLine($"  Interior : {interior}");
Console.WriteLine();
foreach (var c in result.Classifications.Where(c => c.IsExterior))
{
    Console.WriteLine($"  [EXT] {c.Element.IfcType,-30} {c.Element.GlobalId}");
}

// Evaluation against ground truth — van der Vaart (2022) counting + Ying et al. (2022) Precision/Recall.
if (File.Exists(gtPath))
{
    var groundTruth = GroundTruthCsvReader.Read(gtPath);
    var counts      = MetricsCalculator.Compute(result.Classifications, groundTruth);
    var skipped     = result.Classifications.Count - counts.Total;

    Console.WriteLine();
    Console.WriteLine($"Evaluation vs {Path.GetFileName(gtPath)}:");
    Console.WriteLine($"  TP={counts.TruePositives}  FP={counts.FalsePositives}  " +
                      $"FN={counts.FalseNegatives}  TN={counts.TrueNegatives}");
    Console.WriteLine($"  Precision = {Format(counts.Precision)}");
    Console.WriteLine($"  Recall    = {Format(counts.Recall)}");
    Console.WriteLine($"  (skipped {skipped} classifications with no ground-truth entry)");

    static string Format(double v) => double.IsNaN(v) ? "—" : v.ToString("F3");
}

#if DEBUG
Console.WriteLine();
Console.WriteLine("Debug viewer still serving. Press any key to exit...");
Console.ReadKey();
#endif

return;

static string? FindUpward(string relative)
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir is not null)
    {
        var candidate = Path.Combine(dir.FullName, relative);
        if (File.Exists(candidate))
        {
            return candidate;
        }

        dir = dir.Parent;
    }

    return null;
}
