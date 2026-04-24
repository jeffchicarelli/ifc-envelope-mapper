using g4;
using IfcEnvelopeMapper.Core.Domain.Voxel;

namespace IfcEnvelopeMapper.Core.Extensions;

public static class VoxelGrid3DExtensions
{
    // One DMesh3 containing a cube per voxel coord, optionally shrunk to help
    // the eye parse neighboring voxels. `shrinkFactor` ∈ (0,1] — 1.0 is
    // cells-touching, 0.25 (the default debug policy) leaves a gap 3× wider
    // than the voxel itself. Batching into a single mesh keeps the viewer
    // scene graph flat (one layer per call, not one per voxel).
    public static DMesh3 CubesAt(this VoxelGrid3D grid, IEnumerable<VoxelCoord> coords, double shrinkFactor = 1.0)
    {
        var mesh = new DMesh3();
        foreach (var coord in coords)
        {
            var box = grid.GetVoxelBox(coord);
            AxisAlignedBox3dExtensions.AppendCube(mesh, Shrink(box, shrinkFactor));
        }
        return mesh;
    }

    private static AxisAlignedBox3d Shrink(AxisAlignedBox3d box, double factor)
    {
        if (Math.Abs(factor - 1.0) < 1e-9)
        {
            return box;
        }

        var center = box.Center;
        var half   = new Vector3d(box.Width * 0.5, box.Height * 0.5, box.Depth * 0.5) * factor;
        return new AxisAlignedBox3d(center - half, center + half);
    }
}
