using g4;
using IfcEnvelopeMapper.Domain.Voxel;

namespace IfcEnvelopeMapper.Domain.Tests.Voxel;

public sealed class VoxelGrid3DTests
{
    // 1x1x1 m cube, 0.5 m voxels → 2x2x2 grid
    private static VoxelGrid3D MakeGrid() =>
        new(new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(1, 1, 1)), voxelSize: 0.5);

    [Fact]
    public void Constructor_ComputesDimensions()
    {
        var grid = MakeGrid();

        grid.NX.Should().Be(2);
        grid.NY.Should().Be(2);
        grid.NZ.Should().Be(2);
    }

    [Fact]
    public void Constructor_AllStatesUnknown()
    {
        var grid = MakeGrid();

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
        var grid   = MakeGrid();
        var center = new Vector3d(0.25, 0.25, 0.25);

        var coord = grid.WorldToVoxel(center);

        coord.Should().Be(new VoxelCoord(0, 0, 0));
    }

    [Fact]
    public void WorldToVoxel_OutsideBounds_ReturnsNull()
    {
        var grid = MakeGrid();

        var coord = grid.WorldToVoxel(new Vector3d(2, 2, 2));

        coord.Should().BeNull();
    }

    [Fact]
    public void VoxelToCenter_OriginVoxel_ReturnsCenterPoint()
    {
        var grid   = MakeGrid();

        var center = grid.VoxelToCenter(new VoxelCoord(0, 0, 0));

        center.x.Should().BeApproximately(0.25, 1e-9);
        center.y.Should().BeApproximately(0.25, 1e-9);
        center.z.Should().BeApproximately(0.25, 1e-9);
    }

    [Fact]
    public void Neighbors26_CornerVoxel_Returns7Neighbors()
    {
        var grid   = MakeGrid();
        var corner = new VoxelCoord(0, 0, 0);

        var neighbors = grid.Neighbors26(corner).ToList();

        // corner of 2x2x2 has exactly 7 in-bounds neighbors
        neighbors.Should().HaveCount(7);
    }

    [Fact]
    public void Neighbors26_CenterVoxel_Returns26Neighbors()
    {
        // 3x3x3 grid so (1,1,1) has all 26 neighbors in bounds
        var grid   = new VoxelGrid3D(
            new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(1.5, 1.5, 1.5)),
            voxelSize: 0.5);
        var center = new VoxelCoord(1, 1, 1);

        var neighbors = grid.Neighbors26(center).ToList();

        neighbors.Should().HaveCount(26);
    }

    [Fact]
    public void AddOccupant_VoxelTracksGlobalId()
    {
        var grid  = MakeGrid();
        var coord = new VoxelCoord(0, 0, 0);

        grid.AddOccupant(coord, "wall-001");

        grid.OccupantsOf(coord).Should().Contain("wall-001");
    }

    [Fact]
    public void OccupantsOf_EmptyVoxel_ReturnsEmptySet()
    {
        var grid = MakeGrid();

        var occupants = grid.OccupantsOf(new VoxelCoord(0, 0, 0));

        occupants.Should().BeEmpty();
    }

    [Fact]
    public void VoxelsOccupiedBy_ReturnsOnlyMatchingCoords()
    {
        var grid = MakeGrid();
        grid.AddOccupant(new VoxelCoord(0, 0, 0), "wall-001");
        grid.AddOccupant(new VoxelCoord(1, 1, 1), "wall-001");
        grid.AddOccupant(new VoxelCoord(0, 1, 0), "door-002");

        var coords = grid.VoxelsOccupiedBy("wall-001").ToList();

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
        var grid = MakeHollowBox();

        grid.GrowExterior();

        grid[new VoxelCoord(0, 0, 0)].Should().Be(VoxelState.Exterior);
    }

    [Fact]
    public void GrowExterior_WallVoxels_StayOccupied()
    {
        var grid = MakeHollowBox();

        grid.GrowExterior();

        grid[new VoxelCoord(1, 1, 1)].Should().Be(VoxelState.Occupied);
        grid[new VoxelCoord(3, 3, 3)].Should().Be(VoxelState.Occupied);
    }

    [Fact]
    public void GrowExterior_InteriorVoxel_StaysUnknown()
    {
        var grid = MakeHollowBox();

        grid.GrowExterior();

        // BFS cannot reach (2,2,2) through Occupied walls
        grid[new VoxelCoord(2, 2, 2)].Should().Be(VoxelState.Unknown);
    }

    [Fact]
    public void GrowInterior_AfterGrowExterior_InteriorVoxelBecomesInterior()
    {
        var grid = MakeHollowBox();
        grid.GrowExterior();

        grid.GrowInterior();

        grid[new VoxelCoord(2, 2, 2)].Should().Be(VoxelState.Interior);
    }

    [Fact]
    public void GrowVoid_SingleRoom_AssignsRoomId1()
    {
        var grid = MakeHollowBox();
        grid.GrowExterior();
        grid.GrowInterior();

        grid.GrowVoid();

        grid.GetRoomId(new VoxelCoord(2, 2, 2)).Should().Be(1);
    }

    [Fact]
    public void FillGaps_UnknownSandwichedByOccupied_BecomesOccupied()
    {
        // (1,1,1) sandwiched by Occupied on all three axis pairs
        var grid = new VoxelGrid3D(
            new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(3, 3, 3)),
            voxelSize: 1.0);

        grid[new VoxelCoord(0, 1, 1)] = VoxelState.Occupied;
        grid[new VoxelCoord(2, 1, 1)] = VoxelState.Occupied;
        grid[new VoxelCoord(1, 0, 1)] = VoxelState.Occupied;
        grid[new VoxelCoord(1, 2, 1)] = VoxelState.Occupied;
        grid[new VoxelCoord(1, 1, 0)] = VoxelState.Occupied;
        grid[new VoxelCoord(1, 1, 2)] = VoxelState.Occupied;

        grid.FillGaps();

        grid[new VoxelCoord(1, 1, 1)].Should().Be(VoxelState.Occupied);
    }

    [Fact]
    public void FillGaps_UnknownWithNoCompleteAxisPair_StaysUnknown()
    {
        // One Occupied neighbor on each axis, but never both sides;
        // no axis pair is complete so FillGaps has nothing to close.
        var grid = new VoxelGrid3D(
            new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(3, 3, 3)),
            voxelSize: 1.0);

        grid[new VoxelCoord(0, 1, 1)] = VoxelState.Occupied; // +X only
        grid[new VoxelCoord(1, 0, 1)] = VoxelState.Occupied; // +Y only
        grid[new VoxelCoord(1, 1, 0)] = VoxelState.Occupied; // +Z only

        grid.FillGaps();

        grid[new VoxelCoord(1, 1, 1)].Should().Be(VoxelState.Unknown);
    }
}
