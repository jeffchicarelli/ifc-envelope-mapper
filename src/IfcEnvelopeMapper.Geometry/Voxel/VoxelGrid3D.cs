using g4;

namespace IfcEnvelopeMapper.Geometry.Voxel;

public sealed class VoxelGrid3D
{
    private readonly VoxelState[,,] _states;
    private readonly HashSet<string>[,,] _occupants;

    public AxisAlignedBox3d Bounds { get; }
    public double VoxelSize { get; }
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

    public VoxelCoord? WorldToVoxel(Vector3d point)
    {
        var local = point - Bounds.Min;
        var x = (int)(local.x / VoxelSize);
        var y = (int)(local.y / VoxelSize);
        var z = (int)(local.z / VoxelSize);
        var c = new VoxelCoord(x, y, z);
        return IsInBounds(c) ? c : null;
    }

    public Vector3d VoxelToCenter(VoxelCoord c) =>
        Bounds.Min + new Vector3d(
            (c.X + 0.5) * VoxelSize,
            (c.Y + 0.5) * VoxelSize,
            (c.Z + 0.5) * VoxelSize);

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

    public void GrowExterior() => throw new NotImplementedException();
    public void GrowInterior() => throw new NotImplementedException();
    public void GrowVoid()     => throw new NotImplementedException();
    public void FillGaps()     => throw new NotImplementedException();
}
