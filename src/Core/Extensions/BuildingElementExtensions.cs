using g4;
using IfcEnvelopeMapper.Core.Domain.Element;

namespace IfcEnvelopeMapper.Core.Extensions;

public static class BuildingElementExtensions
{
    // Combined AABB of every element's mesh. Used by the voxel grid to size
    // its bounding box before rasterizing.
    public static AxisAlignedBox3d BoundingBox(this IEnumerable<BuildingElement> elements)
    {
        var list = elements as IList<BuildingElement> ?? elements.ToList();
        if (list.Count == 0) throw new InvalidOperationException("Cannot compute BoundingBox of empty element collection.");

        var bbox = list[0].Mesh.GetBounds();
        for (var i = 1; i < list.Count; i++)
        {
            bbox.Contain(list[i].Mesh.GetBounds());
        }
        return bbox;
    }
}
