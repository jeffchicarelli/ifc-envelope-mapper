using System.CommandLine;
using System.Diagnostics;
using IfcEnvelopeMapper.Core.Diagnostics;
using IfcEnvelopeMapper.Engine.Pipeline.BcfReport;
using IfcEnvelopeMapper.Engine.Pipeline.Detection;
using IfcEnvelopeMapper.Engine.Pipeline.JsonReport;
using IfcEnvelopeMapper.Engine.Debug.Api;
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

var outputOption = new Option<FileInfo?>(
    name: "--output",
    description: "Path for the JSON report. If omitted, no file is written.");
outputOption.AddAlias("-o");

var detectCmd = new Command("detect", "Run envelope detection on an IFC model")
{
    inputOption,
    voxelSizeOption,
    strategyOption,
    outputOption,
};
detectCmd.SetHandler(RunDetect, inputOption, voxelSizeOption, strategyOption, outputOption);

var root = new RootCommand("ifcenvmapper — IFC building envelope mapper")
{
    detectCmd,
};

return await root.InvokeAsync(args);

static void RunDetect(FileInfo input, double voxelSize, string strategy, FileInfo? output)
{
    Console.WriteLine($"Opening: {input.FullName}");

    IEnvelopeDetector impl;
    StrategyConfig     config;
    switch (strategy)
    {
        case "voxel":
            impl   = new VoxelFloodFillStrategy(voxelSize: voxelSize);
            config = new StrategyConfig(VoxelSize: voxelSize, NumRays: null, JitterDeg: null, HitRatio: null);
            Console.WriteLine($"Running VoxelFloodFillStrategy (voxelSize={voxelSize:F3} m)...");
            break;
        case "raycast":
            impl   = new RayCastingStrategy();
            config = new StrategyConfig(VoxelSize: null, NumRays: 8, JitterDeg: 5.0, HitRatio: 0.5);
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

    if (output is not null)
    {
        switch (output.Extension.ToLowerInvariant())
        {
            case ".json":
                var report = ReportBuilder.Build(input.FullName, strategy, config, result, sw.Elapsed);
                JsonReportWriter.Write(report, output.FullName);
                break;
            case ".bcf":
            case ".bcfzip":
                BcfWriter.Write(BcfBuilder.Build(result), output.FullName);
                break;
            default:
                throw new ArgumentException(
                    $"Unsupported output format: {output.Extension}. Use .json or .bcf.");
        }
        Console.WriteLine();
        Console.WriteLine($"Report written to: {output.FullName}");
    }
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
