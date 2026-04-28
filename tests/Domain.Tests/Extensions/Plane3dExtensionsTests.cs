using g4;
using IfcEnvelopeMapper.Domain.Extensions;

namespace IfcEnvelopeMapper.Domain.Tests.Extensions;

public class Plane3DExtensionsTests
{
    private const double TOLERANCE = 1e-10;

    [Fact]
    public void ToQuadMesh_ProducesFourVerticesAndTwoTriangles()
    {
        var plane = new Plane3d(Vector3d.AxisZ, Vector3d.Zero);

        var mesh = plane.ToQuadMesh(displaySize: 1.0);

        mesh.VertexCount.Should().Be(4);
        mesh.TriangleCount.Should().Be(2);
    }

    [Fact]
    public void ToQuadMesh_AllVerticesLieOnThePlane()
    {
        // Oblique plane through a non-origin point — exercises the general case.
        var plane = new Plane3d(new Vector3d(1, 2, 3).Normalized, new Vector3d(4, 5, 6));

        var mesh = plane.ToQuadMesh(displaySize: 3.0);

        for (var vid = 0; vid < mesh.VertexCount; vid++)
        {
            var signedDistance = Math.Abs(plane.Normal.Dot(mesh.GetVertex(vid)) - plane.Constant);
            signedDistance.Should().BeLessThan(TOLERANCE);
        }
    }

    [Fact]
    public void ToQuadMesh_CentroidIsAtFootOfPerpendicularFromWorldOrigin()
    {
        // Plane z = 5. Foot of perpendicular from (0,0,0) = (0,0,5) = Normal * Constant.
        var plane = new Plane3d(Vector3d.AxisZ, new Vector3d(0, 0, 5));

        var mesh = plane.ToQuadMesh(displaySize: 2.0);

        var centroid = Vector3d.Zero;
        for (var vid = 0; vid < mesh.VertexCount; vid++)
        {
            centroid += mesh.GetVertex(vid);
        }

        centroid /= mesh.VertexCount;

        var expected = plane.Normal * plane.Constant;
        (centroid - expected).Length.Should().BeLessThan(TOLERANCE);
    }

    [Fact]
    public void ToQuadMesh_AllFourEdgesHaveLengthEqualToDisplaySize()
    {
        const double displaySize = 2.5;
        var plane = new Plane3d(Vector3d.AxisZ, Vector3d.Zero);

        var mesh = plane.ToQuadMesh(displaySize);

        // Vertices appended in order a, b, c, d → ids 0..3 — adjacent pairs form the four sides.
        var a = mesh.GetVertex(0);
        var b = mesh.GetVertex(1);
        var c = mesh.GetVertex(2);
        var d = mesh.GetVertex(3);

        (b - a).Length.Should().BeApproximately(displaySize, TOLERANCE);
        (c - b).Length.Should().BeApproximately(displaySize, TOLERANCE);
        (d - c).Length.Should().BeApproximately(displaySize, TOLERANCE);
        (a - d).Length.Should().BeApproximately(displaySize, TOLERANCE);
    }

    [Fact]
    public void ToQuadMesh_NormalOnYAxis_FallsBackToXAsSeedAndStillProducesValidQuad()
    {
        // When |normal·Y| >= 0.99 the method picks X as the seed axis instead of Y.
        var plane = new Plane3d(Vector3d.AxisY, Vector3d.Zero);

        var mesh = plane.ToQuadMesh(displaySize: 1.0);

        mesh.VertexCount.Should().Be(4);
        for (var vid = 0; vid < mesh.VertexCount; vid++)
        {
            Math.Abs(mesh.GetVertex(vid).y).Should().BeLessThan(TOLERANCE);
        }
    }
}
