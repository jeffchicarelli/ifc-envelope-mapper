using g4;
using IfcEnvelopeMapper.Domain.Voxel;

namespace IfcEnvelopeMapper.Domain.Extensions;

/// <summary>Extension methods on <see cref="Voxel.VoxelGrid3D"/>.</summary>
public static class VoxelGrid3DExtensions
{
    private const double IDENTITY_EPSILON = 1e-9;

    /// <summary>
    /// One <see cref="DMesh3"/> containing a cube per voxel coord, optionally shrunk so neighbouring voxels don't visually fuse. Batching into a
    /// single mesh keeps the viewer scene graph flat (one layer per call, not one node per voxel).
    /// </summary>
    /// <param name="coords">Coordinates of voxels to include in the mesh.</param>
    /// <param name="shrinkFactor">
    /// Value in (0, 1]. 1.0 = cells touch (the default). Callers visualising dense voxel clouds typically pass 0.25 to leave a
    /// gap three times the voxel width.
    /// </param>
    /// <param name="grid">Voxel grid containing the voxels to be meshed.</param>
    public static DMesh3 CubesAt(this VoxelGrid3D grid, IEnumerable<VoxelCoord> coords, double shrinkFactor = 1.0)
    {
        var mesh = new DMesh3();

        foreach (var coord in coords)
        {
            var box = grid.GetVoxelBox(coord);

            AxisAlignedBox3DExtensions.AppendCube(mesh, Shrink(box, shrinkFactor));
        }

        return mesh;
    }

    private static AxisAlignedBox3d Shrink(AxisAlignedBox3d box, double factor)
    {
        if (Math.Abs(factor - 1.0) < IDENTITY_EPSILON)
        {
            return box;
        }

        var center = box.Center;

        var half = new Vector3d(box.Width * 0.5, box.Height * 0.5, box.Depth * 0.5) * factor;

        return new AxisAlignedBox3d(center - half, center + half);
    }
}
