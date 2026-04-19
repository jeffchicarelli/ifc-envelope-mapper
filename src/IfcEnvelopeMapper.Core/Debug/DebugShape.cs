using g4;

namespace IfcEnvelopeMapper.Core.Debug;

public abstract class DebugShape
{
    public string? Label { get; init; }
    public DebugColor Color { get; init; }
}

public sealed class DebugMesh(DMesh3 mesh) : DebugShape
{
    public DMesh3 Mesh { get; } = mesh;
}

public sealed class DebugTriangleSelection(DMesh3 mesh, IReadOnlyList<int> triangleIds) : DebugShape
{
    public DMesh3 Mesh { get; } = mesh;
    public IReadOnlyList<int> TriangleIds { get; } = triangleIds;
}

public sealed class DebugPointCloud(IReadOnlyList<Vector3d> points) : DebugShape
{
    public IReadOnlyList<Vector3d> Points { get; } = points;
}

public sealed class DebugVoxelSet(IReadOnlyList<Vector3d> centers, double voxelSize) : DebugShape
{
    public IReadOnlyList<Vector3d> Centers { get; } = centers;
    public double VoxelSize { get; } = voxelSize;
}

public sealed class DebugRay(Vector3d origin, Vector3d direction) : DebugShape
{
    public Vector3d Origin { get; } = origin;
    public Vector3d Direction { get; } = direction;
}

public sealed class DebugEdge(Vector3d a, Vector3d b) : DebugShape
{
    public Vector3d A { get; } = a;
    public Vector3d B { get; } = b;
}

public sealed class DebugSphere(Vector3d center, double radius) : DebugShape
{
    public Vector3d Center { get; } = center;
    public double Radius { get; } = radius;
}
