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

    // 5x5x5 grid (indices 0-4). Outer layer: free exterior shell.
    // Hollow box walls at indices 1 and 3: Occupied.
    // Interior: (2,2,2) — unreachable from outside through the walls.
    private static VoxelGrid3D MakeHollowBox()
    {
        var grid = new VoxelGrid3D(
            new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(5, 5, 5)),
            voxelSize: 1.0);

        for (var x = 1; x <= 3; x++)
        {
            for (var y = 1; y <= 3; y++)
            {
                for (var z = 1; z <= 3; z++)
                {
                    if (x == 1 || x == 3 || y == 1 || y == 3 || z == 1 || z == 3)
                    {
                        grid[new VoxelCoord(x, y, z)] = VoxelState.Occupied;
                    }
                }
            }
        }

        return grid;
    }

    [Fact]
    public void GrowExterior_CornerVoxel_BecomesExterior()
    {
        // Arrange
        var grid = MakeHollowBox();

        // Act
        grid.GrowExterior();

        // Assert
        grid[new VoxelCoord(0, 0, 0)].Should().Be(VoxelState.Exterior);
    }

    [Fact]
    public void GrowExterior_WallVoxels_StayOccupied()
    {
        // Arrange
        var grid = MakeHollowBox();

        // Act
        grid.GrowExterior();

        // Assert
        grid[new VoxelCoord(1, 1, 1)].Should().Be(VoxelState.Occupied);
        grid[new VoxelCoord(3, 3, 3)].Should().Be(VoxelState.Occupied);
    }

    [Fact]
    public void GrowExterior_InteriorVoxel_StaysUnknown()
    {
        // Arrange
        var grid = MakeHollowBox();

        // Act
        grid.GrowExterior();

        // Assert — BFS cannot reach (2,2,2) through Occupied walls
        grid[new VoxelCoord(2, 2, 2)].Should().Be(VoxelState.Unknown);
    }

    [Fact]
    public void GrowInterior_AfterGrowExterior_InteriorVoxelBecomesInterior()
    {
        // Arrange
        var grid = MakeHollowBox();
        grid.GrowExterior();

        // Act
        grid.GrowInterior();

        // Assert
        grid[new VoxelCoord(2, 2, 2)].Should().Be(VoxelState.Interior);
    }

    [Fact]
    public void GrowVoid_SingleRoom_AssignsRoomId1()
    {
        // Arrange
        var grid = MakeHollowBox();
        grid.GrowExterior();
        grid.GrowInterior();

        // Act
        grid.GrowVoid();

        // Assert
        grid.GetRoomId(new VoxelCoord(2, 2, 2)).Should().Be(1);
    }

    [Fact]
    public void FillGaps_UnknownSurroundedByExterior_BecomesExterior()
    {
        // Arrange — (1,1,1) surrounded on all 6 faces by Exterior
        var grid = new VoxelGrid3D(
            new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(3, 3, 3)),
            voxelSize: 1.0);

        grid[new VoxelCoord(0, 1, 1)] = VoxelState.Exterior;
        grid[new VoxelCoord(2, 1, 1)] = VoxelState.Exterior;
        grid[new VoxelCoord(1, 0, 1)] = VoxelState.Exterior;
        grid[new VoxelCoord(1, 2, 1)] = VoxelState.Exterior;
        grid[new VoxelCoord(1, 1, 0)] = VoxelState.Exterior;
        grid[new VoxelCoord(1, 1, 2)] = VoxelState.Exterior;

        // Act
        grid.FillGaps();

        // Assert
        grid[new VoxelCoord(1, 1, 1)].Should().Be(VoxelState.Exterior);
    }

    [Fact]
    public void FillGaps_UnknownWithFiveExteriorNeighbors_StaysUnknown()
    {
        // Arrange — only 5 of 6 face-adjacent voxels are Exterior
        var grid = new VoxelGrid3D(
            new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(3, 3, 3)),
            voxelSize: 1.0);

        grid[new VoxelCoord(0, 1, 1)] = VoxelState.Exterior;
        grid[new VoxelCoord(2, 1, 1)] = VoxelState.Exterior;
        grid[new VoxelCoord(1, 0, 1)] = VoxelState.Exterior;
        grid[new VoxelCoord(1, 2, 1)] = VoxelState.Exterior;
        grid[new VoxelCoord(1, 1, 0)] = VoxelState.Exterior;

        // (1,1,2) left as Unknown — only 5 Exterior neighbors

        // Act
        grid.FillGaps();

        // Assert
        grid[new VoxelCoord(1, 1, 1)].Should().Be(VoxelState.Unknown);
    }
}
