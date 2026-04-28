using System.CommandLine;
using System.Diagnostics;
using IfcEnvelopeMapper.Application.Reports;
using IfcEnvelopeMapper.Domain.Detection;
using IfcEnvelopeMapper.Domain.Services;
using IfcEnvelopeMapper.Infrastructure.Detection;
using IfcEnvelopeMapper.Infrastructure.Ifc.Loading;
using IfcEnvelopeMapper.Infrastructure.Persistence;

namespace IfcEnvelopeMapper.Cli.Commands;

/// <summary>
/// CLI command that runs envelope detection on an IFC file.
/// <list type="bullet">
/// <item>
/// <description>Wires the <c>--input</c>, <c>--voxel-size</c>, <c>--strategy</c>, and <c>--output</c> options.</description>
/// </item>
/// <item>
/// <description>Dispatches to the chosen <see cref="IEnvelopeDetector"/> (voxel flood-fill or ray casting).</description>
/// </item>
/// <item>
/// <description>Prints a console summary of exterior/interior counts and optionally writes a JSON or BCF report.</description>
/// </item>
/// </list>
/// </summary>
public static class DetectCommand
{
    /// <summary>Builds the <c>detect</c> sub-command for the root CLI.</summary>
    public static Command Build()
    {
        var inputOption = new Option<FileInfo>("--input", "Path to the IFC file to analyze") { IsRequired = true };
        inputOption.AddAlias("-i");

        var voxelSizeOption = new Option<double>("--voxel-size", () => 0.25, "Voxel size in meters (only used by --strategy voxel)");
        voxelSizeOption.AddAlias("-v");

        var strategyOption = new Option<string>("--strategy", () => "voxel", "Detection strategy: voxel (primary) or raycast (baseline).");
        strategyOption.AddAlias("-s");
        strategyOption.FromAmong("voxel", "raycast");

        var outputOption = new Option<FileInfo?>("--output", "Path for the JSON report. If omitted, no file is written.");
        outputOption.AddAlias("-o");

        var cmd = new Command("detect", "Run envelope detection on an IFC model") { inputOption, voxelSizeOption, strategyOption, outputOption };
        cmd.SetHandler(Run, inputOption, voxelSizeOption, strategyOption, outputOption);

        return cmd;
    }

    private static void Run(FileInfo input, double voxelSize, string strategy, FileInfo? output)
    {
        Console.WriteLine($"Opening: {input.FullName}");

        IEnvelopeDetector impl;

        switch (strategy)
        {
            case "voxel":
                impl = new VoxelFloodFillDetector(voxelSize);
                Console.WriteLine($"Running VoxelFloodFillDetector (voxelSize={voxelSize:F3} m)...");
                break;
            case "raycast":
                impl = new RayCastingDetector();
                Console.WriteLine("Running RayCastingDetector (numRays=8, jitterDeg=5°, hitRatio=0.5)...");
                break;
            default:
                throw new InvalidOperationException($"Unknown strategy: {strategy}");
        }

        var loader = new XbimModelLoader();
        using var model = loader.Load(input.FullName);

        var sw = Stopwatch.StartNew();
        var result = impl.Detect(model.Elements);
        sw.Stop();

        PrintReport(result, sw.Elapsed);

        if (output is not null)
        {
            switch (output.Extension.ToLowerInvariant())
            {
                case ".json":
                    var report = JsonReportBuilder.Build(input.FullName, strategy, impl.Config, result, sw.Elapsed);
                    new JsonReportWriter().Write(report, output.FullName);
                    break;
                case ".bcf":
                case ".bcfzip":
                    new BcfWriter().Write(BcfBuilder.Build(result), output.FullName);
                    break;
                default:
                    throw new ArgumentException($"Unsupported output format: {output.Extension}. Use .json or .bcf.");
            }

            Console.WriteLine();
            Console.WriteLine($"Report written to: {output.FullName}");
        }
    }

    private static void PrintReport(DetectionResult result, TimeSpan elapsed)
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
}
