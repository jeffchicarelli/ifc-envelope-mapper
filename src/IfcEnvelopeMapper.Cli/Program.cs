using Xbim.Common.XbimExtensions;
using Xbim.Ifc;
using Xbim.Ifc4.Interfaces;
using Xbim.ModelGeometry.Scene;

var ifcPath = FindUpward("data/models/duplex.ifc")
    ?? throw new FileNotFoundException("duplex.ifc not found in any parent directory");

Console.WriteLine($"Opening: {ifcPath}");

using var model = IfcStore.Open(ifcPath);

var elements = model.Instances.OfType<IIfcBuildingElement>().ToList();
Console.WriteLine($"IIfcBuildingElement count: {elements.Count}");

foreach (var element in elements)
{
    Console.WriteLine($"IfcType: {element.GetType().Name} Id: {element.GlobalId}");
}

// --- Slice 3 ---
Console.WriteLine();
Console.WriteLine("Building Xbim3DModelContext...");

var context = new Xbim3DModelContext(model);
context.CreateContext();
Console.WriteLine("CreateContext() done.");

var firstWall = model.Instances.OfType<IIfcWall>().FirstOrDefault();
// Console.WriteLine($"firstWall null: {firstWall is null}");
// if (firstWall is null)
// {
//     Console.WriteLine("No walls found.");
//     return;
// }

Console.WriteLine($"First wall: {firstWall.GetType().Name} {firstWall.GlobalId}");

var wallInstances = context.ShapeInstances()
    .Where(si => si.IfcProductLabel == firstWall.EntityLabel)
    .ToList();

Console.WriteLine($"  ShapeInstances for this wall: {wallInstances.Count}");

var totalTriangles = 0;
var totalVertices = 0;

foreach (var instance in wallInstances)
{
    var geometry = context.ShapeGeometry(instance);
    if (geometry is null || string.IsNullOrEmpty(geometry.ShapeData)) continue;

    // Console.WriteLine($"  Geometry for this wall: {geometry.ShapeData}");

    using var ms = new MemoryStream(geometry.ToByteArray());
    using var br = new BinaryReader(ms);
    var triangulation = br.ReadShapeTriangulation();

    var faceGroups = triangulation.Faces.Count;
    var triCount = triangulation.Faces.Sum(f => f.TriangleCount);
    var vertCount = triangulation.Vertices.Count;

    Console.WriteLine($"    vertices: {vertCount}, face-groups: {faceGroups}, triangles: {triCount}");
    totalTriangles += triCount;
    totalVertices += vertCount;

    for (var i = 0; i < triangulation.Faces.Count; i++)
    {
        var face = triangulation.Faces[i];
        if (face.IsPlanar)
        {
            var n = face.Normals[0].Normal;
            Console.WriteLine($"      face[{i}] Normal=({n.X:F3}, {n.Y:F3}, {n.Z:F3}) triangles={face.TriangleCount}");
        }
        else
        {
            Console.WriteLine($"      face[{i}] (non-planar, {face.NormalCount} vertex-normals) triangles={face.TriangleCount}");
        }
    }
}

Console.WriteLine($"  Totals — vertices: {totalVertices}, triangles: {totalTriangles}");
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
