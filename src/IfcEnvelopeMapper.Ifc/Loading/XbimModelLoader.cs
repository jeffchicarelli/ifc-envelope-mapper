using g4;
using IfcEnvelopeMapper.Core.Element;
using IfcEnvelopeMapper.Core.Loading;
using Xbim.Common.Geometry;
using Xbim.Common.XbimExtensions;
using Xbim.Ifc;
using Xbim.Ifc4.Interfaces;
using Xbim.ModelGeometry.Scene;

namespace IfcEnvelopeMapper.Ifc.Loading;

public sealed class XbimModelLoader : IModelLoader
{
    private readonly IElementFilter _filter;

    public XbimModelLoader(IElementFilter? filter = null)
    {
        _filter = filter ?? new DefaultElementFilter();
    }

    public ModelLoadResult Load(string path)
    {
        IfcStore model;
        try
        {
            model = IfcStore.Open(path);
        }
        catch (Exception ex)
        {
            throw new IfcLoadException(path, $"Failed to open IFC file: {path}", ex);
        }

        using (model)
        {
            Xbim3DModelContext context;
            try
            {
                context = new Xbim3DModelContext(model);
                context.MaxThreads = 1;
                context.CreateContext();
            }
            catch (Exception ex)
            {
                throw new IfcGeometryException(path, $"Failed to create geometry context: {path}", ex);
            }

            var elements = new List<BuildingElement>();
            var groups = new List<BuildingElementGroup>();

            foreach (var ifcElem in model.Instances.OfType<IIfcBuildingElement>())
            {
                if (!_filter.Include(ifcElem.GetType().Name))
                {
                    continue;
                }

                // Skip if this element is an included child — its parent will pick it up.
                // (Not in the plan's pseudocode yet; avoids duplicating aggregated children.)
                var isChildOfIncluded = ifcElem.Decomposes
                                               .Any(r => r.RelatingObject is IIfcBuildingElement parent
                                                         && _filter.Include(parent.GetType().Name));
                if (isChildOfIncluded)
                {
                    continue;
                }

                var ctx = ExtractContext(ifcElem);

                var children = ifcElem.IsDecomposedBy
                                      .SelectMany(r => r.RelatedObjects.OfType<IIfcBuildingElement>())
                                      .Where(c => _filter.Include(c.GetType().Name))
                                      .ToList();

                if (children.Count == 0)
                {
                    var mesh = ExtractMesh(ifcElem, context);
                    if (mesh.TriangleCount > 0)
                    {
                        elements.Add(new BuildingElement
                        {
                            GlobalId = ifcElem.GlobalId,
                            IfcType = ifcElem.GetType().Name,
                            Mesh = mesh,
                            Context = ctx,
                        });
                    }
                }
                else
                {
                    var groupId = ifcElem.GlobalId;
                    var groupElements = new List<BuildingElement>();

                    foreach (var child in children)
                    {
                        var childMesh = ExtractMesh(child, context);
                        if (childMesh.TriangleCount == 0)
                        {
                            // TODO P3: logger.Warning — element discarded (empty mesh).
                            continue;
                        }

                        var elem = new BuildingElement
                        {
                            GlobalId = child.GlobalId,
                            IfcType = child.GetType().Name,
                            Mesh = childMesh,
                            Context = ExtractContext(child),
                            GroupGlobalId = groupId,
                        };
                        elements.Add(elem);
                        groupElements.Add(elem);
                    }

                    var ownMesh = ExtractMesh(ifcElem, context);
                    groups.Add(new BuildingElementGroup
                    {
                        GlobalId = groupId,
                        IfcType = ifcElem.GetType().Name,
                        Context = ctx,
                        OwnMesh = ownMesh.TriangleCount > 0 ? ownMesh : null,
                        Elements = groupElements,
                    });
                }
            }

            return new ModelLoadResult(elements, groups);
        }
    }

    private static BuildingElementContext ExtractContext(IIfcElement elem)
    {
        string? siteId = null;
        string? buildingId = null;
        string? storeyId = null;

        var current = elem.ContainedInStructure
                          .FirstOrDefault()
                         ?.RelatingStructure;

        while (current is not null)
        {
            switch (current)
            {
                case IIfcBuildingStorey s: storeyId ??= s.GlobalId; break;
                case IIfcBuilding b:       buildingId ??= b.GlobalId; break;
                case IIfcSite s:           siteId ??= s.GlobalId; break;
            }

            current = current.Decomposes
                             .FirstOrDefault()
                            ?.RelatingObject as IIfcSpatialElement;
        }

        return new BuildingElementContext(siteId, buildingId, storeyId);
    }

    private static DMesh3 ExtractMesh(IIfcBuildingElement element, Xbim3DModelContext context)
    {
        var mesh = new DMesh3();
        var instances = context.ShapeInstances()
                               .Where(si => si.IfcProductLabel == element.EntityLabel);

        foreach (var instance in instances)
        {
            var geometry = context.ShapeGeometry(instance);
            if (geometry is null || string.IsNullOrEmpty(geometry.ShapeData))
            {
                continue;
            }

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
