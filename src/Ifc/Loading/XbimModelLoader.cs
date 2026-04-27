using g4;
using IfcEnvelopeMapper.Ifc.Domain;
using Microsoft.Extensions.Logging;
using Xbim.Common.Geometry;
using Xbim.Common.XbimExtensions;
using Xbim.Ifc;
using Xbim.Ifc4.Interfaces;
using Xbim.ModelGeometry.Scene;
using static IfcEnvelopeMapper.Core.Diagnostics.AppLog;

namespace IfcEnvelopeMapper.Ifc.Loading;

/// <summary>
/// Loads an IFC file into <see cref="Element"/> instances using <c>Xbim.Ifc</c>
/// for parsing and <c>Xbim.ModelGeometry.Scene</c> for geometry. Mesh and
/// bounding box are deferred via <see cref="Lazy{T}"/>; the
/// <see cref="IfcStore"/> is kept alive by the returned <see cref="ModelLoadResult"/>
/// until disposed. Throws <see cref="IfcLoadException"/> on open failure and
/// <see cref="IfcGeometryException"/> on tessellation failure.
/// </summary>
public sealed class XbimModelLoader
{
    private readonly ElementFilter _filter;

    public XbimModelLoader(ElementFilter? filter = null)
    {
        _filter = filter ?? new ElementFilter();
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

        Log.LogInformation("Opened IFC: {Path}", path);

        Xbim3DModelContext context;
        try
        {
            context = new Xbim3DModelContext(model);
            context.MaxThreads = 1;
            context.CreateContext();
        }
        catch (Exception ex)
        {
            model.Dispose();
            throw new IfcGeometryException(path, $"Failed to create geometry context: {path}", ex);
        }

        var elements = new List<Element>();
        var composites = new List<Element>();

        foreach (var ifcElem in model.Instances.OfType<IIfcBuildingElement>())
        {
            if (!_filter.Include(ifcElem.GetType().Name))
            {
                continue;
            }

            // Skip elements that are children of an INCLUDED parent — the parent walk picks them up.
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
                if (!HasShapeInstances(ifcElem, context))
                {
                    continue;
                }
                elements.Add(BuildElement(ctx, ifcElem, context, groupGlobalId: null));
            }
            else
            {
                var groupId = ifcElem.GlobalId;
                var childElements = new List<Element>();

                foreach (var child in children)
                {
                    if (!HasShapeInstances(child, context))
                    {
                        Log.LogWarning(
                            "Element {GlobalId} ({IfcType}) has no geometry — discarded",
                            child.GlobalId, child.GetType().Name);
                        continue;
                    }

                    var childCtx = ExtractContext(child);
                    var childElem = BuildElement(childCtx, child, context, groupId);
                    childElements.Add(childElem);
                    elements.Add(childElem);
                }

                var composite = BuildElement(ctx, ifcElem, context, groupGlobalId: null);
                composite = new Element(ctx, BuildLazyMesh(ifcElem, context), BuildLazyBbox(ifcElem, context))
                {
                    Children = childElements,
                };
                composites.Add(composite);
            }
        }

        Log.LogInformation(
            "Loaded {ElementCount} elements + {CompositeCount} composites from {Path}",
            elements.Count, composites.Count, path);

        return new ModelLoadResult(model, elements, composites);
    }

    private static Element BuildElement(
        IfcProductContext ctx,
        IIfcBuildingElement product,
        Xbim3DModelContext context,
        string? groupGlobalId)
    {
        return new Element(ctx, BuildLazyMesh(product, context), BuildLazyBbox(product, context))
        {
            GroupGlobalId = groupGlobalId,
        };
    }

    private static Lazy<DMesh3> BuildLazyMesh(IIfcBuildingElement product, Xbim3DModelContext context)
        => new(() => ExtractMesh(product, context), isThreadSafe: true);

    private static Lazy<AxisAlignedBox3d> BuildLazyBbox(IIfcBuildingElement product, Xbim3DModelContext context)
        => new(() => ExtractBbox(product, context), isThreadSafe: true);

    private static bool HasShapeInstances(IIfcBuildingElement element, Xbim3DModelContext context)
        => context.ShapeInstances().Any(si => si.IfcProductLabel == element.EntityLabel);

    private static IfcProductContext ExtractContext(IIfcBuildingElement elem)
    {
        IIfcSite? site = null;
        IIfcBuilding? building = null;
        IIfcBuildingStorey? storey = null;

        var current = elem.ContainedInStructure
                          .FirstOrDefault()
                         ?.RelatingStructure;

        while (current is not null)
        {
            switch (current)
            {
                case IIfcBuildingStorey s: storey ??= s; break;
                case IIfcBuilding b: building ??= b; break;
                case IIfcSite s: site ??= s; break;
            }
            current = current.Decomposes
                             .FirstOrDefault()
                            ?.RelatingObject as IIfcSpatialElement;
        }

        return new IfcProductContext(elem, building, storey, site);
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

    private static AxisAlignedBox3d ExtractBbox(IIfcBuildingElement element, Xbim3DModelContext context)
    {
        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
        var any = false;

        foreach (var instance in context.ShapeInstances()
                                        .Where(si => si.IfcProductLabel == element.EntityLabel))
        {
            var bb = instance.BoundingBox;
            minX = Math.Min(minX, bb.Min.X);
            minY = Math.Min(minY, bb.Min.Y);
            minZ = Math.Min(minZ, bb.Min.Z);
            maxX = Math.Max(maxX, bb.Max.X);
            maxY = Math.Max(maxY, bb.Max.Y);
            maxZ = Math.Max(maxZ, bb.Max.Z);
            any = true;
        }

        return any
            ? new AxisAlignedBox3d(new Vector3d(minX, minY, minZ), new Vector3d(maxX, maxY, maxZ))
            : new AxisAlignedBox3d(Vector3d.Zero, Vector3d.Zero);
    }

    private static void AppendToMesh(
        DMesh3 mesh,
        XbimShapeTriangulation triangulation,
        XbimMatrix3D transform)
    {
        var vertexOffset = mesh.VertexCount;

        foreach (var vertex in triangulation.Vertices)
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
