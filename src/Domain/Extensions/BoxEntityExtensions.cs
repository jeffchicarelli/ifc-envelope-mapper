using g4;
using IfcEnvelopeMapper.Domain.Interfaces;

namespace IfcEnvelopeMapper.Domain.Extensions;

public static class BoxEntityExtensions
{
    /// <summary>
    /// Combined axis-aligned bounding box of every entity's stored bbox — the
    /// union of <see cref="IBoxEntity.GetBoundingBox"/> results. Used by the
    /// voxel grid to size its extents before rasterizing.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the sequence is empty.</exception>
    public static AxisAlignedBox3d BoundingBox(this IEnumerable<IBoxEntity> entities)
    {
        var list = entities as IList<IBoxEntity> ?? entities.ToList();
        if (list.Count == 0)
        {
            throw new InvalidOperationException("Cannot compute BoundingBox of empty entity collection.");
        }

        var bbox = list[0].GetBoundingBox();
        for (var i = 1; i < list.Count; i++)
        {
            bbox.Contain(list[i].GetBoundingBox());
        }

        return bbox;
    }
}
