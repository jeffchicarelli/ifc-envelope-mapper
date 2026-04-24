using g4;
using IfcEnvelopeMapper.Core.Domain.Element;

namespace IfcEnvelopeMapper.Core.Extensions;

public static class BuildingElementExtensions
{
    /// <summary>
    /// Combined axis-aligned bounding box of every element's mesh — the union of
    /// per-mesh <c>GetBounds()</c> rectangles. Used by the voxel grid to size its
    /// extents before rasterizing.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the sequence is empty.</exception>
    public static AxisAlignedBox3d BoundingBox(this IEnumerable<BuildingElement> elements)
    {
        var list = elements as IList<BuildingElement> ?? elements.ToList();
        if (list.Count == 0)
        {
            throw new InvalidOperationException("Cannot compute BoundingBox of empty element collection.");
        }

        var bbox = list[0].Mesh.GetBounds();
        for (var i = 1; i < list.Count; i++)
        {
            bbox.Contain(list[i].Mesh.GetBounds());
        }

        return bbox;
    }
}
