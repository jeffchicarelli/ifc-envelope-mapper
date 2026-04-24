using g4;

namespace IfcEnvelopeMapper.Core.Domain.Voxel;

/// <summary>
/// Uniform 3D grid of cubic voxels covering a world-space bounding box. Each cell
/// stores a <see cref="VoxelState"/>, the set of element GlobalIds that occupy it,
/// and a room id assigned by <see cref="GrowVoid"/>.
///
///               +Z
///                │       ┌──┬──┬──┐
///                │      ╱   ╱   ╱│
///                │     ┌──┬──┬──┐│    NX × NY × NZ cubic cells
///                │    ╱   ╱   ╱│┘│    each of side VoxelSize
///                │   ┌──┬──┬──┐│┘│
///                │   │  │  │  │┘│
///                │   ├──┼──┼──┤┘       indexed by (x, y, z) ∈ [0, N*)
///                │   │  │  │  │
///                │   └──┴──┴──┘
///                └───────────────▶ +X
///              +Y
///
/// The three-phase fill (<see cref="GrowExterior"/> → <see cref="GrowInterior"/>
/// → <see cref="GrowVoid"/>) is the core of the voxel detection strategy
/// (van der Vaart 2022).
/// </summary>
public sealed class VoxelGrid3D
{
    private readonly VoxelState[,,] _states;
    private readonly HashSet<string>[,,] _occupants;
    private readonly int[,,] _roomIds;

    public AxisAlignedBox3d Bounds { get; }
    public double VoxelSize { get; }

    /// <summary>Cell counts along each axis — <c>⌈bounds.Width / VoxelSize⌉</c>, etc.</summary>
    public int NX { get; }
    public int NY { get; }
    public int NZ { get; }

    public VoxelGrid3D(AxisAlignedBox3d bounds, double voxelSize)
    {
        Bounds    = bounds;
        VoxelSize = voxelSize;

        NX = (int)Math.Ceiling(bounds.Width  / voxelSize);
        NY = (int)Math.Ceiling(bounds.Height / voxelSize);
        NZ = (int)Math.Ceiling(bounds.Depth  / voxelSize);

        _states    = new VoxelState[NX, NY, NZ];
        _occupants = new HashSet<string>[NX, NY, NZ];
        _roomIds   = new int[NX, NY, NZ];
    }

    public VoxelState this[VoxelCoord c]
    {
        get => _states[c.X, c.Y, c.Z];
        set => _states[c.X, c.Y, c.Z] = value;
    }

    public bool IsInBounds(VoxelCoord c) =>
        c.X >= 0 && c.X < NX &&
        c.Y >= 0 && c.Y < NY &&
        c.Z >= 0 && c.Z < NZ;

    /// <summary>World-space point → voxel coordinate, or <c>null</c> if the point is outside the grid.</summary>
    public VoxelCoord? WorldToVoxel(Vector3d point)
    {
        var local = point - Bounds.Min;
        var x = (int)(local.x / VoxelSize);
        var y = (int)(local.y / VoxelSize);
        var z = (int)(local.z / VoxelSize);
        var c = new VoxelCoord(x, y, z);
        return IsInBounds(c) ? c : null;
    }

    /// <summary>World-space center of the voxel at <paramref name="c"/>.</summary>
    public Vector3d VoxelToCenter(VoxelCoord c) =>
        Bounds.Min + new Vector3d(
            (c.X + 0.5) * VoxelSize,
            (c.Y + 0.5) * VoxelSize,
            (c.Z + 0.5) * VoxelSize);

    /// <summary>World-space axis-aligned box of the voxel at <paramref name="c"/>.</summary>
    public AxisAlignedBox3d GetVoxelBox(VoxelCoord c)
    {
        var min = Bounds.Min + new Vector3d(c.X * VoxelSize, c.Y * VoxelSize, c.Z * VoxelSize);
        return new AxisAlignedBox3d(min, min + new Vector3d(VoxelSize, VoxelSize, VoxelSize));
    }

    public void AddOccupant(VoxelCoord c, string globalId)
    {
        _occupants[c.X, c.Y, c.Z] ??= new HashSet<string>(StringComparer.Ordinal);
        _occupants[c.X, c.Y, c.Z].Add(globalId);
    }

    public IReadOnlySet<string> OccupantsOf(VoxelCoord c) =>
        _occupants[c.X, c.Y, c.Z] ?? (IReadOnlySet<string>)EmptySet;

    private static readonly IReadOnlySet<string> EmptySet =
        new HashSet<string>(StringComparer.Ordinal);

    public IEnumerable<VoxelCoord> VoxelsOccupiedBy(string globalId)
    {
        for (var x = 0; x < NX; x++)
        {
            for (var y = 0; y < NY; y++)
            {
                for (var z = 0; z < NZ; z++)
                {
                    var occupants = _occupants[x, y, z];
                    if (occupants is not null && occupants.Contains(globalId))
                    {
                        yield return new VoxelCoord(x, y, z);
                    }
                }
            }
        }
    }

    public IEnumerable<VoxelCoord> VoxelsByState(VoxelState state)
    {
        for (var x = 0; x < NX; x++)
        {
            for (var y = 0; y < NY; y++)
            {
                for (var z = 0; z < NZ; z++)
                {
                    if (_states[x, y, z] == state)
                    {
                        yield return new VoxelCoord(x, y, z);
                    }
                }
            }
        }
    }

    public IEnumerable<VoxelCoord> VoxelsInBbox(AxisAlignedBox3d box)
    {
        var min = WorldToVoxel(box.Min) ?? new VoxelCoord(0, 0, 0);
        var max = WorldToVoxel(box.Max) ?? new VoxelCoord(NX - 1, NY - 1, NZ - 1);

        int x0 = Math.Max(0, min.X), x1 = Math.Min(NX - 1, max.X);
        int y0 = Math.Max(0, min.Y), y1 = Math.Min(NY - 1, max.Y);
        int z0 = Math.Max(0, min.Z), z1 = Math.Min(NZ - 1, max.Z);

        for (var x = x0; x <= x1; x++)
        {
            for (var y = y0; y <= y1; y++)
            {
                for (var z = z0; z <= z1; z++)
                {
                    yield return new VoxelCoord(x, y, z);
                }
            }
        }
    }

    /// <summary>The up to 26 in-bounds neighbors around <paramref name="c"/> (faces + edges + corners).</summary>
    public IEnumerable<VoxelCoord> Neighbors26(VoxelCoord c)
    {
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dz = -1; dz <= 1; dz++)
                {
                    if (dx == 0 && dy == 0 && dz == 0)
                    {
                        continue;
                    }

                    var n = new VoxelCoord(c.X + dx, c.Y + dy, c.Z + dz);
                    if (IsInBounds(n))
                    {
                        yield return n;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Phase 1 — 26-connected flood fill from voxel <c>(0,0,0)</c>, marking every
    /// reachable <c>Unknown</c> cell as <see cref="VoxelState.Exterior"/>.
    /// </summary>
    /// <remarks>
    /// <c>(0,0,0)</c> is guaranteed to start outside the model: callers expand the
    /// building's bounding box by <c>2 × voxelSize</c> before constructing the grid,
    /// so there is always a free shell around the geometry.
    /// </remarks>
    public void GrowExterior()
    {
        var queue = new Queue<VoxelCoord>();
        var seed = new VoxelCoord(0, 0, 0);
        this[seed] = VoxelState.Exterior;
        queue.Enqueue(seed);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var neighbor in Neighbors26(current))
            {
                if (this[neighbor] == VoxelState.Unknown)
                {
                    this[neighbor] = VoxelState.Exterior;
                    queue.Enqueue(neighbor);
                }
            }
        }
    }

    /// <summary>
    /// Phase 2 — any voxel still <see cref="VoxelState.Unknown"/> after
    /// <see cref="GrowExterior"/> was never reached from outside, so it must be
    /// an interior. Must run after <see cref="GrowExterior"/>.
    /// </summary>
    public void GrowInterior()
    {
        for (var x = 0; x < NX; x++)
        {
            for (var y = 0; y < NY; y++)
            {
                for (var z = 0; z < NZ; z++)
                {
                    var c = new VoxelCoord(x, y, z);
                    if (this[c] == VoxelState.Unknown)
                    {
                        this[c] = VoxelState.Interior;
                    }
                }
            }
        }
    }

    public int GetRoomId(VoxelCoord c) => _roomIds[c.X, c.Y, c.Z];

    /// <summary>
    /// Phase 3 — each connected region of <see cref="VoxelState.Interior"/> voxels
    /// becomes a distinct room numbered from 1 upward. Must run after
    /// <see cref="GrowInterior"/>.
    /// </summary>
    public void GrowVoid()
    {
        var roomId = 0;
        for (var x = 0; x < NX; x++)
        {
            for (var y = 0; y < NY; y++)
            {
                for (var z = 0; z < NZ; z++)
                {
                    var seed = new VoxelCoord(x, y, z);
                    if (this[seed] != VoxelState.Interior || _roomIds[x, y, z] != 0)
                    {
                        continue;
                    }

                    roomId++;
                    var queue = new Queue<VoxelCoord>();
                    _roomIds[x, y, z] = roomId;
                    queue.Enqueue(seed);

                    while (queue.Count > 0)
                    {
                        var current = queue.Dequeue();
                        foreach (var neighbor in Neighbors26(current))
                        {
                            if (this[neighbor] == VoxelState.Interior
                                && _roomIds[neighbor.X, neighbor.Y, neighbor.Z] == 0)
                            {
                                _roomIds[neighbor.X, neighbor.Y, neighbor.Z] = roomId;
                                queue.Enqueue(neighbor);
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Closes 1-voxel gaps in <see cref="VoxelState.Occupied"/> shells caused by
    /// imperfect IFC meshes. An <see cref="VoxelState.Unknown"/> voxel whose six
    /// face-adjacent neighbors are all <see cref="VoxelState.Exterior"/> is itself
    /// flipped to <c>Exterior</c>. Iterates until no change happens.
    /// </summary>
    public void FillGaps()
    {
        bool changed;
        do
        {
            changed = false;
            for (var x = 0; x < NX; x++)
            {
                for (var y = 0; y < NY; y++)
                {
                    for (var z = 0; z < NZ; z++)
                    {
                        var c = new VoxelCoord(x, y, z);
                        if (this[c] != VoxelState.Unknown)
                        {
                            continue;
                        }

                        var exteriorNeighbors = Neighbors6(c)
                           .Count(n => this[n] == VoxelState.Exterior);

                        if (exteriorNeighbors >= 6)
                        {
                            this[c] = VoxelState.Exterior;
                            changed = true;
                        }
                    }
                }
            }
        } while (changed);
    }

    private IEnumerable<VoxelCoord> Neighbors6(VoxelCoord c)
    {
        VoxelCoord[] candidates =
        [
            new(c.X - 1, c.Y, c.Z),
            new(c.X + 1, c.Y, c.Z),
            new(c.X, c.Y - 1, c.Z),
            new(c.X, c.Y + 1, c.Z),
            new(c.X, c.Y, c.Z - 1),
            new(c.X, c.Y, c.Z + 1)
        ];
        return candidates.Where(IsInBounds);
    }
}
