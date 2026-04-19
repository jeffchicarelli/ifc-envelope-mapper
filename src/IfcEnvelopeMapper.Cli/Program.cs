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

Console.WriteLine($"Loaded {model.Elements.Count} elements");
foreach (var element in model.Elements)
{
    Console.WriteLine($"  {element.IfcType}  {element.GlobalId}");
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
