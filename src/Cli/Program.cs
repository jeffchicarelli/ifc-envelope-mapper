using System.CommandLine;
using System.Diagnostics;
using IfcEnvelopeMapper.Core.Diagnostics;
using IfcEnvelopeMapper.Core.Pipeline.Detection;
using IfcEnvelopeMapper.Engine.Strategies;
using IfcEnvelopeMapper.Engine.Visualization;
using IfcEnvelopeMapper.Ifc.Loading;
using Microsoft.Extensions.Logging;
using Xbim.Common.Configuration;

// CLI is the production runner: no viewer helper, no GLB output. The debug
// emission path is reserved for xunit tests (which keep the default
// Enabled=true and produce their own per-test disagreement GLBs).
GeometryDebug.Enabled = false;

using var loggerFactory = LoggerFactory.Create(b => b
    .AddConsole()
    .SetMinimumLevel(LogLevel.Warning)
    .AddFilter("IfcEnvelopeMapper", LogLevel.Information));

AppLog.Configure(loggerFactory);

XbimServices.Current.ConfigureServices(s => s
    .AddXbimToolkit(c => c.AddLoggerFactory(loggerFactory)));

var inputOption = new Option<FileInfo>(
        name: "--input",
        description: "Path to the IFC file to analyze")
    { IsRequired = true };
inputOption.AddAlias("-i");

var voxelSizeOption = new Option<double>(
    name: "--voxel-size",
    getDefaultValue: () => 0.25,
    description: "Voxel size in meters (only used by --strategy voxel)");
voxelSizeOption.AddAlias("-v");

var strategyOption = new Option<string>(
    name: "--strategy",
    getDefaultValue: () => "voxel",
    description: "Detection strategy: voxel (primary) or raycast (baseline).");
strategyOption.AddAlias("-s");
strategyOption.FromAmong("voxel", "raycast");

var detectCmd = new Command("detect", "Run envelope detection on an IFC model")
{
    inputOption,
    voxelSizeOption,
    strategyOption,
};
detectCmd.SetHandler(RunDetect, inputOption, voxelSizeOption, strategyOption);

var root = new RootCommand("ifcenvmapper — IFC building envelope mapper")
{
    detectCmd,
};

return await root.InvokeAsync(args);

static void RunDetect(FileInfo input, double voxelSize, string strategy)
{
    Console.WriteLine($"Opening: {input.FullName}");

    IDetectionStrategy impl;
    switch (strategy)
    {
        case "voxel":
            impl = new VoxelFloodFillStrategy(voxelSize: voxelSize);
            Console.WriteLine($"Running VoxelFloodFillStrategy (voxelSize={voxelSize:F3} m)...");
            break;
        case "raycast":
            impl = new RayCastingStrategy();
            Console.WriteLine("Running RayCastingStrategy (numRays=8, jitterDeg=5°, hitRatio=0.5)...");
            break;
        default:
            throw new InvalidOperationException($"Unknown strategy: {strategy}");
    }

    var loader = new XbimModelLoader();
    var model = loader.Load(input.FullName);

    var sw = Stopwatch.StartNew();
    var result = impl.Detect(model.Elements);
    sw.Stop();

    PrintReport(result, sw.Elapsed);
}

static void PrintReport(DetectionResult result, TimeSpan elapsed)
{
    var ext = result.Classifications.Count(c => c.IsExterior);
    var intr = result.Classifications.Count(c => !c.IsExterior);

    Console.WriteLine($"Done in {elapsed.TotalSeconds:F2}s");
    Console.WriteLine($"  Exterior : {ext}");
    Console.WriteLine($"  Interior : {intr}");
    Console.WriteLine();
    foreach (var c in result.Classifications.Where(c => c.IsExterior))
    {
        Console.WriteLine($"  [EXT] {c.Element.IfcType,-30} {c.Element.GlobalId}");
    }
}
