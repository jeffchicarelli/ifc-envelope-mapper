using g4;
using IfcEnvelopeMapper.Core.Extensions;

namespace IfcEnvelopeMapper.Tests.Core.Extensions;

public class AxisAlignedBox3DExtensionsTests
{
    private const double TOLERANCE = 1e-10;

    private static readonly AxisAlignedBox3d _sampleBox =
        new(new Vector3d(1, 2, 3), new Vector3d(5, 7, 10));  // W=4, H=5, D=7

    private static readonly Vector3d[] _expectedCorners =
    {
        new(1, 2, 3),  new(5, 2, 3),  new(5, 2, 10), new(1, 2, 10),
        new(1, 7, 3),  new(5, 7, 3),  new(5, 7, 10), new(1, 7, 10),
    };

    // ───── ToCube ─────

    [Fact]
    public void ToCube_ProducesEightVerticesAndTwelveTriangles()
    {
        var mesh = _sampleBox.ToCube();

        mesh.VertexCount.Should().Be(8);
        mesh.TriangleCount.Should().Be(12);
    }

    [Fact]
    public void ToCube_VertexSetEqualsTheEightBoxCorners()
    {
        var mesh = _sampleBox.ToCube();

        var actual = new HashSet<Vector3d>();
        for (var vid = 0; vid < mesh.VertexCount; vid++)
        {
            actual.Add(mesh.GetVertex(vid));
        }

        actual.Should().BeEquivalentTo(_expectedCorners);
    }

    [Fact]
    public void ToCube_ProducesAClosedMesh()
    {
        // A watertight cube: every edge must be shared by exactly 2 triangles.
        var mesh = _sampleBox.ToCube();

        mesh.IsClosed().Should().BeTrue();
    }

    // ───── ToWireframe ─────

    [Fact]
    public void ToWireframe_YieldsExactlyTwelveEdges()
    {
        var edges = _sampleBox.ToWireframe().ToList();

        edges.Should().HaveCount(12);
    }

    [Fact]
    public void ToWireframe_EveryEndpointIsOneOfTheEightBoxCorners()
    {
        var corners = _expectedCorners.ToHashSet();

        foreach (var (from, to) in _sampleBox.ToWireframe())
        {
            corners.Should().Contain(from);
            corners.Should().Contain(to);
        }
    }

    [Fact]
    public void ToWireframe_EdgeLengthDistribution_Is4xWidth_4xHeight_4xDepth()
    {
        var edges = _sampleBox.ToWireframe().ToList();

        var lengths = edges.Select(e => (e.To - e.From).Length).ToList();

        lengths.Count(l => Math.Abs(l - _sampleBox.Width)  < TOLERANCE).Should().Be(4);
        lengths.Count(l => Math.Abs(l - _sampleBox.Height) < TOLERANCE).Should().Be(4);
        lengths.Count(l => Math.Abs(l - _sampleBox.Depth)  < TOLERANCE).Should().Be(4);
    }
}
