using System.Diagnostics;
using IfcEnvelopeMapper.Core.Pipeline.Evaluation;
using IfcEnvelopeMapper.Engine.Strategies;
using IfcEnvelopeMapper.Ifc.Evaluation;
using Microsoft.Extensions.Logging;
using Xbim.Common.Configuration;

#if DEBUG
using IfcEnvelopeMapper.Engine.Visualization;

// Trigger the debug viewer's static ctor (binds port 5173, writes empty GLB)
// before the pipeline starts so the viewer has something to serve immediately.
GeometryDebug.Clear();
#endif

using var loggerFactory = LoggerFactory.Create(b =>
    b.AddConsole().SetMinimumLevel(LogLevel.Warning));

XbimServices.Current.ConfigureServices(s =>
    s.AddXbimToolkit(c => c.AddLoggerFactory(loggerFactory)));

var ifcPath = FindUpward("data/models/duplex.ifc")
              ?? throw new FileNotFoundException("duplex.ifc not found in any parent directory");

var gtPath = Path.Combine(
    Path.GetDirectoryName(Path.GetDirectoryName(ifcPath))!,
    "ground-truth", "duplex.csv");

const double voxelSize = 0.25;

Console.WriteLine($"Opening: {ifcPath}");
Console.WriteLine($"Running VoxelFloodFillStrategy (voxelSize={voxelSize})...");

var sw = Stopwatch.StartNew();
var evaluation = EvaluationPipeline.EvaluateDetection(
    ifcPath,
    gtPath,
    new VoxelFloodFillStrategy(voxelSize: voxelSize));
sw.Stop();

PrintReport(evaluation, sw.Elapsed, gtPath);

#if DEBUG
Console.WriteLine();
Console.WriteLine("Debug viewer still serving. Press any key to exit...");
Console.ReadKey();
#endif

return;

static void PrintReport(EvaluationResult evaluation, TimeSpan elapsed, string gtPath)
{
    var classifications = evaluation.Detection.Classifications;
    var ext = classifications.Count(c => c.IsExterior);
    var intr = classifications.Count(c => !c.IsExterior);
    var counts = evaluation.Counts;
    var skipped = classifications.Count - counts.Total;

    Console.WriteLine($"Done in {elapsed.TotalSeconds:F2}s");
    Console.WriteLine($"  Exterior : {ext}");
    Console.WriteLine($"  Interior : {intr}");
    Console.WriteLine();
    foreach (var c in classifications.Where(c => c.IsExterior))
    {
        Console.WriteLine($"  [EXT] {c.Element.IfcType,-30} {c.Element.GlobalId}");
    }

    Console.WriteLine();
    Console.WriteLine($"Evaluation vs {Path.GetFileName(gtPath)}:");
    Console.WriteLine($"  TP={counts.TruePositives}  FP={counts.FalsePositives}  " +
                      $"FN={counts.FalseNegatives}  TN={counts.TrueNegatives}");
    Console.WriteLine($"  Precision = {Format(counts.Precision)}");
    Console.WriteLine($"  Recall    = {Format(counts.Recall)}");
    Console.WriteLine($"  (skipped {skipped} classifications with no ground-truth entry)");

    static string Format(double v) => double.IsNaN(v) ? "—" : v.ToString("F3");
}

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
