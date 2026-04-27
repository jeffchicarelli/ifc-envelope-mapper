using System.CommandLine;
using System.Diagnostics;
using IfcEnvelopeMapper.Engine.Pipeline.BcfReport;
using IfcEnvelopeMapper.Engine.Pipeline.Detection;
using IfcEnvelopeMapper.Engine.Pipeline.JsonReport;
using IfcEnvelopeMapper.Ifc.Loading;

namespace IfcEnvelopeMapper.Cli.Commands;

/// <summary>
/// CLI command that runs envelope detection on an IFC file.
/// <list type="bullet">
/// <item><description>Wires the <c>--input</c>, <c>--voxel-size</c>,
/// <c>--strategy</c>, and <c>--output</c> options.</description></item>
/// <item><description>Dispatches to the chosen <see cref="IEnvelopeDetector"/>
/// (voxel flood-fill or ray casting).</description></item>
/// <item><description>Prints a console summary of exterior/interior counts and
/// optionally writes a JSON or BCF report.</description></item>
/// </list>
/// </summary>
public static class DetectCommand
{
    /// <summary>Builds the <c>detect</c> sub-command for the root CLI.</summary>
    public static Command Build()
    {
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

        var cmd = new Command("detect", "Run envelope detection on an IFC model")
        {
            inputOption,
            voxelSizeOption,
            strategyOption,
            outputOption,
        };
        cmd.SetHandler(Run, inputOption, voxelSizeOption, strategyOption, outputOption);
        return cmd;
    }

    private static void Run(FileInfo input, double voxelSize, string strategy, FileInfo? output)
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

    private static void PrintReport(DetectionResult result, TimeSpan elapsed)
    {
        var ext  = result.Classifications.Count(c => c.IsExterior);
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
}
