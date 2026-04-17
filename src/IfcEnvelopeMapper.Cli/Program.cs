using IfcEnvelopeMapper.Core.Pipeline;
using IfcEnvelopeMapper.Ifc;

var ifcPath = FindUpward("data/models/duplex.ifc")
    ?? throw new FileNotFoundException("duplex.ifc not found in any parent directory");

Console.WriteLine($"Opening: {ifcPath}");

IModelLoader loader = new XbimModelLoader();
var elements = loader.Load(ifcPath);

var first = elements.First(e => e.Mesh.TriangleCount > 0);
Console.WriteLine($"First with geometry: {first.IfcType} {first.GlobalId} " +
                  $"tris={first.Mesh.TriangleCount} " +
                  $"bbox=({first.BoundingBox.Min}) → ({first.BoundingBox.Max})");

Console.WriteLine($"Loaded {elements.Count} elements");
foreach (var element in elements)
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
