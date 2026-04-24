using g4;
using IfcEnvelopeMapper.Core.Domain.Voxel;
using IfcEnvelopeMapper.Core.Extensions;

namespace IfcEnvelopeMapper.Tests.Core.Extensions;

public class VoxelGrid3DExtensionsTests
{
    private const double TOLERANCE = 1e-10;

    private static VoxelGrid3D MakeGrid()
    {
        // 2×2×2 grid of unit voxels from (0,0,0) to (2,2,2).
        var bounds = new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(2, 2, 2));
        return new VoxelGrid3D(bounds, voxelSize: 1.0);
    }

    [Fact]
    public void CubesAt_EmptyCoords_ReturnsEmptyMesh()
    {
        var grid = MakeGrid();

        var mesh = grid.CubesAt(Array.Empty<VoxelCoord>());

        mesh.VertexCount.Should().Be(0);
        mesh.TriangleCount.Should().Be(0);
    }

    [Fact]
    public void CubesAt_ThreeCoords_Produces24VerticesAnd36Triangles()
    {
        var grid = MakeGrid();
        var coords = new[]
        {
            new VoxelCoord(0, 0, 0),
            new VoxelCoord(1, 0, 0),
            new VoxelCoord(1, 1, 1),
        };

        var mesh = grid.CubesAt(coords);

        mesh.VertexCount.Should().Be(8 * 3);
        mesh.TriangleCount.Should().Be(12 * 3);
    }

    [Fact]
    public void CubesAt_SingleCoord_EveryVertexSitsOnTheVoxelMinOrMaxPlane()
    {
        var grid = MakeGrid();
        var coord = new VoxelCoord(1, 0, 0);
        var expected = grid.GetVoxelBox(coord);

        var mesh = grid.CubesAt(new[] { coord });

        for (var vid = 0; vid < mesh.VertexCount; vid++)
        {
            var v = mesh.GetVertex(vid);
            (Near(v.x, expected.Min.x) || Near(v.x, expected.Max.x)).Should().BeTrue();
            (Near(v.y, expected.Min.y) || Near(v.y, expected.Max.y)).Should().BeTrue();
            (Near(v.z, expected.Min.z) || Near(v.z, expected.Max.z)).Should().BeTrue();
        }
    }

    [Fact]
    public void CubesAt_ShrinkFactorOne_BoundingBoxMatchesVoxelBoxExactly()
    {
        var grid = MakeGrid();
        var coord = new VoxelCoord(0, 0, 0);
        var expected = grid.GetVoxelBox(coord);

        var mesh = grid.CubesAt(new[] { coord }, shrinkFactor: 1.0);

        var (min, max) = BoundingBox(mesh);
        (min - expected.Min).Length.Should().BeLessThan(TOLERANCE);
        (max - expected.Max).Length.Should().BeLessThan(TOLERANCE);
    }

    [Fact]
    public void CubesAt_ShrinkFactorHalf_EveryVertexLiesStrictlyInsideTheVoxel()
    {
        var grid = MakeGrid();
        var coord = new VoxelCoord(0, 0, 0);
        var voxel = grid.GetVoxelBox(coord);

        var mesh = grid.CubesAt(new[] { coord }, shrinkFactor: 0.5);

        for (var vid = 0; vid < mesh.VertexCount; vid++)
        {
            var v = mesh.GetVertex(vid);
            v.x.Should().BeGreaterThan(voxel.Min.x).And.BeLessThan(voxel.Max.x);
            v.y.Should().BeGreaterThan(voxel.Min.y).And.BeLessThan(voxel.Max.y);
            v.z.Should().BeGreaterThan(voxel.Min.z).And.BeLessThan(voxel.Max.z);
        }
    }

    // ───── helpers ─────

    private static bool Near(double a, double b) => Math.Abs(a - b) < TOLERANCE;

    private static (Vector3d Min, Vector3d Max) BoundingBox(DMesh3 mesh)
    {
        var minX = double.PositiveInfinity; var maxX = double.NegativeInfinity;
        var minY = double.PositiveInfinity; var maxY = double.NegativeInfinity;
        var minZ = double.PositiveInfinity; var maxZ = double.NegativeInfinity;

        for (var vid = 0; vid < mesh.VertexCount; vid++)
        {
            var v = mesh.GetVertex(vid);
            if (v.x < minX) minX = v.x; if (v.x > maxX) maxX = v.x;
            if (v.y < minY) minY = v.y; if (v.y > maxY) maxY = v.y;
            if (v.z < minZ) minZ = v.z; if (v.z > maxZ) maxZ = v.z;
        }
        return (new Vector3d(minX, minY, minZ), new Vector3d(maxX, maxY, maxZ));
    }
}
