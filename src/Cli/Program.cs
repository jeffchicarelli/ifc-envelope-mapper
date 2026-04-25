using System.CommandLine;
using System.Diagnostics;
using IfcEnvelopeMapper.Core.Diagnostics;
using IfcEnvelopeMapper.Core.Pipeline.Detection;
using IfcEnvelopeMapper.Engine.Strategies;
using IfcEnvelopeMapper.Ifc.Loading;
using Microsoft.Extensions.Logging;
using Xbim.Common.Configuration;

#if DEBUG
using IfcEnvelopeMapper.Engine.Visualization;

// Trigger the debug viewer's static ctor (binds port 5173, writes empty GLB)
// before the pipeline starts, so the viewer has something to serve immediately.
GeometryDebug.Clear();
#endif

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

var detectCmd = new Command("detect", "Run envelope detection on an IFC model")
{
    inputOption,
    voxelSizeOption,
};
detectCmd.SetHandler(RunDetect, inputOption, voxelSizeOption);

var root = new RootCommand("ifcenvmapper — IFC building envelope mapper")
{
    detectCmd,
};

return await root.InvokeAsync(args);

static void RunDetect(FileInfo input, double voxelSize)
{
    Console.WriteLine($"Opening: {input.FullName}");
    Console.WriteLine($"Running VoxelFloodFillStrategy (voxelSize={voxelSize:F3} m)...");

    var loader = new XbimModelLoader();
    var model = loader.Load(input.FullName);

    var sw = Stopwatch.StartNew();
    var strategy = new VoxelFloodFillStrategy(voxelSize: voxelSize);
    var result = strategy.Detect(model.Elements);
    sw.Stop();

    PrintReport(result, sw.Elapsed);

#if DEBUG
    Console.WriteLine();
    Console.WriteLine("Debug viewer still serving. Press any key to exit...");
    Console.ReadKey();
#endif
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
