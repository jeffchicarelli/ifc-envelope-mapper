using g4;

namespace IfcEnvelopeMapper.Domain.Interfaces;

/// <summary>
/// Provides a precomputed, world-coordinate axis-aligned bounding box.
/// </summary>
public interface IBoxEntity
{
    AxisAlignedBox3d GetBoundingBox();
}
