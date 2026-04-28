using g4;
using IfcEnvelopeMapper.Domain.Extensions;

namespace IfcEnvelopeMapper.Domain.Tests.Extensions;

public class Vector3DExtensionsTests
{
    private const double TOLERANCE = 1e-10;

    // ───── FitPlane ─────

    [Fact]
    public void FitPlane_PointsOnXYPlane_NormalIsParallelToZ()
    {
        var points = new[]
        {
            new Vector3d(0, 0, 0),
            new Vector3d(1, 0, 0),
            new Vector3d(0, 1, 0),
            new Vector3d(1, 1, 0),
        };

        var plane = points.FitPlane();

        Math.Abs(plane.Normal.Dot(Vector3d.AxisZ)).Should().BeApproximately(1.0, TOLERANCE);
    }

    [Fact]
    public void FitPlane_PointsOnXZPlane_NormalIsParallelToY()
    {
        var points = new[]
        {
            new Vector3d(0, 0, 0),
            new Vector3d(1, 0, 0),
            new Vector3d(0, 0, 1),
            new Vector3d(1, 0, 1),
        };

        var plane = points.FitPlane();

        Math.Abs(plane.Normal.Dot(Vector3d.AxisY)).Should().BeApproximately(1.0, TOLERANCE);
    }

    [Fact]
    public void FitPlane_ResultPassesThroughEveryInputPoint()
    {
        // All four points lie on z = 5. Plane3d uses Hessian form
        // Normal · X = Constant, so signed distance is Normal·P − Constant.
        var points = new[]
        {
            new Vector3d(2, 3, 5),
            new Vector3d(4, 3, 5),
            new Vector3d(2, 7, 5),
            new Vector3d(6, 1, 5),
        };

        var plane = points.FitPlane();

        foreach (var p in points)
        {
            var distance = Math.Abs(plane.Normal.Dot(p) - plane.Constant);
            distance.Should().BeLessThan(TOLERANCE);
        }
    }

    [Fact]
    public void FitPlane_AcceptsLazyEnumerable()
    {
        // Select() yields a non-IList sequence, forcing the `?? points.ToList()`
        // fallback inside FitPlane.
        IEnumerable<Vector3d> lazy = Enumerable.Range(0, 4)
            .Select(i => new Vector3d(i, i * 2, 0))
            .Concat(new[] { new Vector3d(-1, 1, 0) });

        var plane = lazy.FitPlane();

        plane.Normal.Length.Should().BeApproximately(1.0, TOLERANCE);
    }

    // ───── ToSphere ─────

    [Fact]
    public void ToSphere_DefaultParameters_HasExpectedVertexAndTriangleCounts()
    {
        const int rings   = 8;
        const int sectors = 12;

        var mesh = Vector3d.Zero.ToSphere(radius: 1.0);

        mesh.VertexCount.Should().Be((rings + 1) * sectors);
        mesh.TriangleCount.Should().Be(2 * rings * sectors);
    }

    [Fact]
    public void ToSphere_AllVerticesAreAtRadiusFromCenter()
    {
        var center = new Vector3d(10, 20, 30);
        const double radius = 2.5;

        var mesh = center.ToSphere(radius);

        for (var vid = 0; vid < mesh.VertexCount; vid++)
        {
            var distance = (mesh.GetVertex(vid) - center).Length;
            distance.Should().BeApproximately(radius, TOLERANCE);
        }
    }

    [Fact]
    public void ToSphere_CustomRingsAndSectors_ScalesVertexAndTriangleCounts()
    {
        const int rings   = 4;
        const int sectors = 6;

        var mesh = Vector3d.Zero.ToSphere(1.0, rings, sectors);

        mesh.VertexCount.Should().Be((rings + 1) * sectors);
        mesh.TriangleCount.Should().Be(2 * rings * sectors);
    }

    [Fact]
    public void ToSphere_ZeroRadius_CollapsesAllVerticesToCenter()
    {
        var center = new Vector3d(5, 5, 5);

        var mesh = center.ToSphere(radius: 0);

        for (var vid = 0; vid < mesh.VertexCount; vid++)
        {
            (mesh.GetVertex(vid) - center).Length.Should().BeLessThan(TOLERANCE);
        }
    }
}
