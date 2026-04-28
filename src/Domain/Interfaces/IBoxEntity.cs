using g4;

namespace IfcEnvelopeMapper.Domain.Interfaces;

/// <summary>Provides a precomputed, world-coordinate axis-aligned bounding box.</summary>
public interface IBoxEntity
{
    /// <summary>Returns the world-space axis-aligned bounding box.</summary>
    AxisAlignedBox3d GetBoundingBox();
}
