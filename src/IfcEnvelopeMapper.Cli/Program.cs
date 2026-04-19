using System.Diagnostics;
using IfcEnvelopeMapper.Algorithms.Detection;
using IfcEnvelopeMapper.Core.Loading;
using IfcEnvelopeMapper.Ifc.Loading;

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

Console.WriteLine("Running VoxelFloodFillStrategy (voxelSize=0.5)...");
var sw = Stopwatch.StartNew();
var result = new VoxelFloodFillStrategy(voxelSize: 0.5).Detect(model.Elements);
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
