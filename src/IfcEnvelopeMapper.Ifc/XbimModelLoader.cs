using g4;
using IfcEnvelopeMapper.Core.Building;
using IfcEnvelopeMapper.Core.Pipeline;
using Xbim.Common.Geometry;
using Xbim.Common.XbimExtensions;
using Xbim.Ifc;
using Xbim.Ifc4.Interfaces;
using Xbim.ModelGeometry.Scene;

namespace IfcEnvelopeMapper.Ifc;

public sealed class XbimModelLoader : IModelLoader
{
    public ModelLoadResult Load(string path)
    {
        using var model = IfcStore.Open(path);
        var context = new Xbim3DModelContext(model);
        // OCCT has a thread-unsafe handle teardown path that races under the
        // default parallel geometry worker, producing AccessViolationException
        // in WriteShapeGeometries. Force single-threaded processing.
        context.MaxThreads = 1;
        context.CreateContext();

        var result = new List<BuildingElement>();
        foreach (var element in model.Instances.OfType<IIfcBuildingElement>())
        {
            var mesh = ExtractMesh(element, context);
            result.Add(new BuildingElement
            {
                GlobalId = element.GlobalId,
                IfcType = element.GetType().Name,
                Mesh = mesh
            });
        }

        return new ModelLoadResult(result, []);
    }

    private static DMesh3 ExtractMesh(IIfcBuildingElement element, Xbim3DModelContext context)
    {
        var mesh = new DMesh3();
        var instances = context.ShapeInstances()
                               .Where(si => si.IfcProductLabel == element.EntityLabel);

        foreach (var instance in instances)
        {
            var geometry = context.ShapeGeometry(instance);
            if (geometry is null || string.IsNullOrEmpty(geometry.ShapeData)) continue;

            using var ms = new MemoryStream(geometry.ToByteArray());
            using var br = new BinaryReader(ms);
            var triangulation = br.ReadShapeTriangulation();

            AppendToMesh(mesh, triangulation, instance.Transformation);
        }

        return mesh;
    }

    private static void AppendToMesh(
        DMesh3 mesh,
        XbimShapeTriangulation triangulation,
        XbimMatrix3D transform)
    {
        var vertexOffset = mesh.VertexCount;

        var vertices = triangulation.Vertices;

        foreach (var vertex in vertices)
        {
            var v = transform.Transform(vertex);
            mesh.AppendVertex(new Vector3d(v.X, v.Y, v.Z));
        }

        foreach (var face in triangulation.Faces)
        {
            var indices = face.Indices;
            for (var i = 0; i < indices.Count; i += 3)
            {
                mesh.AppendTriangle(new Index3i(
                    vertexOffset + indices[i],
                    vertexOffset + indices[i + 1],
                    vertexOffset + indices[i + 2]));
            }
        }
    }
}
