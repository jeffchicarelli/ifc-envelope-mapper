using g4;
using IfcEnvelopeMapper.Geometry.Voxel;

namespace IfcEnvelopeMapper.Tests.Geometry.Voxel;

public sealed class VoxelGrid3DTests
{
    // 1x1x1 m cube, 0.5 m voxels → 2x2x2 grid
    private static VoxelGrid3D MakeGrid() =>
        new(new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(1, 1, 1)), voxelSize: 0.5);

    [Fact]
    public void Constructor_ComputesDimensions()
    {
        // Arrange + Act
        var grid = MakeGrid();

        // Assert
        grid.NX.Should().Be(2);
        grid.NY.Should().Be(2);
        grid.NZ.Should().Be(2);
    }

    [Fact]
    public void Constructor_AllStatesUnknown()
    {
        // Arrange + Act
        var grid = MakeGrid();

        // Assert
        for (var x = 0; x < grid.NX; x++)
        {
            for (var y = 0; y < grid.NY; y++)
            {
                for (var z = 0; z < grid.NZ; z++)
                {
                    grid[new VoxelCoord(x, y, z)].Should().Be(VoxelState.Unknown);
                }
            }
        }
    }

    [Fact]
    public void WorldToVoxel_CenterOfFirstVoxel_ReturnsOrigin()
    {
        // Arrange
        var grid = MakeGrid();
        var center = new Vector3d(0.25, 0.25, 0.25);

        // Act
        var coord = grid.WorldToVoxel(center);

        // Assert
        coord.Should().Be(new VoxelCoord(0, 0, 0));
    }

    [Fact]
    public void WorldToVoxel_OutsideBounds_ReturnsNull()
    {
        // Arrange
        var grid = MakeGrid();

        // Act
        var coord = grid.WorldToVoxel(new Vector3d(2, 2, 2));

        // Assert
        coord.Should().BeNull();
    }

    [Fact]
    public void VoxelToCenter_OriginVoxel_ReturnsCenterPoint()
    {
        // Arrange
        var grid = MakeGrid();

        // Act
        var center = grid.VoxelToCenter(new VoxelCoord(0, 0, 0));

        // Assert
        center.x.Should().BeApproximately(0.25, 1e-9);
        center.y.Should().BeApproximately(0.25, 1e-9);
        center.z.Should().BeApproximately(0.25, 1e-9);
    }

    [Fact]
    public void Neighbors26_CornerVoxel_Returns7Neighbors()
    {
        // Arrange
        var grid = MakeGrid();
        var corner = new VoxelCoord(0, 0, 0);

        // Act
        var neighbors = grid.Neighbors26(corner).ToList();

        // Assert — corner of 2x2x2 has exactly 7 in-bounds neighbors
        neighbors.Should().HaveCount(7);
    }

    [Fact]
    public void Neighbors26_CenterVoxel_Returns26Neighbors()
    {
        // Arrange — 3x3x3 grid so (1,1,1) has all 26 neighbors in bounds
        var grid = new VoxelGrid3D(
            new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(1.5, 1.5, 1.5)),
            voxelSize: 0.5);
        var center = new VoxelCoord(1, 1, 1);

        // Act
        var neighbors = grid.Neighbors26(center).ToList();

        // Assert
        neighbors.Should().HaveCount(26);
    }

    [Fact]
    public void AddOccupant_VoxelTracksGlobalId()
    {
        // Arrange
        var grid = MakeGrid();
        var coord = new VoxelCoord(0, 0, 0);

        // Act
        grid.AddOccupant(coord, "wall-001");

        // Assert
        grid.OccupantsOf(coord).Should().Contain("wall-001");
    }

    [Fact]
    public void OccupantsOf_EmptyVoxel_ReturnsEmptySet()
    {
        // Arrange
        var grid = MakeGrid();

        // Act
        var occupants = grid.OccupantsOf(new VoxelCoord(0, 0, 0));

        // Assert
        occupants.Should().BeEmpty();
    }

    [Fact]
    public void VoxelsOccupiedBy_ReturnsOnlyMatchingCoords()
    {
        // Arrange
        var grid = MakeGrid();
        grid.AddOccupant(new VoxelCoord(0, 0, 0), "wall-001");
        grid.AddOccupant(new VoxelCoord(1, 1, 1), "wall-001");
        grid.AddOccupant(new VoxelCoord(0, 1, 0), "door-002");

        // Act
        var coords = grid.VoxelsOccupiedBy("wall-001").ToList();

        // Assert
        coords.Should().HaveCount(2);
        coords.Should().Contain(new VoxelCoord(0, 0, 0));
        coords.Should().Contain(new VoxelCoord(1, 1, 1));
    }
}
