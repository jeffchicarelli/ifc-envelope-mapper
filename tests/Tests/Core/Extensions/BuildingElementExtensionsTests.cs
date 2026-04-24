using g4;
using IfcEnvelopeMapper.Core.Domain.Element;
using IfcEnvelopeMapper.Core.Extensions;

namespace IfcEnvelopeMapper.Tests.Core.Extensions;

public class BuildingElementExtensionsTests
{
    private const double TOLERANCE = 1e-10;

    private static BuildingElement MakeElement(string id, Vector3d min, Vector3d max)
    {
        // Minimal two-triangle mesh whose bounds exactly equal (min, max).
        var mesh = new DMesh3();
        var v0 = mesh.AppendVertex(min);
        var v1 = mesh.AppendVertex(new Vector3d(max.x, min.y, min.z));
        var v2 = mesh.AppendVertex(max);
        var v3 = mesh.AppendVertex(new Vector3d(min.x, max.y, max.z));
        mesh.AppendTriangle(new Index3i(v0, v1, v2));
        mesh.AppendTriangle(new Index3i(v0, v2, v3));

        return new BuildingElement
        {
            GlobalId = id,
            IfcType = "IfcWall",
            Mesh = mesh,
        };
    }

    [Fact]
    public void BoundingBox_EmptySequence_Throws()
    {
        var empty = Enumerable.Empty<BuildingElement>();

        var act = () => empty.BoundingBox();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void BoundingBox_SingleElement_MatchesThatElementsMeshBounds()
    {
        var e = MakeElement("a", new Vector3d(1, 2, 3), new Vector3d(4, 5, 6));

        var bbox = new[] { e }.BoundingBox();

        (bbox.Min - new Vector3d(1, 2, 3)).Length.Should().BeLessThan(TOLERANCE);
        (bbox.Max - new Vector3d(4, 5, 6)).Length.Should().BeLessThan(TOLERANCE);
    }

    [Fact]
    public void BoundingBox_TwoDisjointElements_IsTheUnionOfBothBoxes()
    {
        // e1 at (0..1)^3, e2 at (10..11)^3 — result must span (0..11) on every axis.
        var e1 = MakeElement("a", Vector3d.Zero, new Vector3d(1, 1, 1));
        var e2 = MakeElement("b", new Vector3d(10, 10, 10), new Vector3d(11, 11, 11));

        var bbox = new[] { e1, e2 }.BoundingBox();

        (bbox.Min - Vector3d.Zero).Length.Should().BeLessThan(TOLERANCE);
        (bbox.Max - new Vector3d(11, 11, 11)).Length.Should().BeLessThan(TOLERANCE);
    }

    [Fact]
    public void BoundingBox_NestedElements_ReturnsOuterBox()
    {
        // Inner box fully contained in outer — result must equal the outer.
        var outer = MakeElement("outer", new Vector3d(-5, -5, -5), new Vector3d(5, 5, 5));
        var inner = MakeElement("inner", new Vector3d(-1, -1, -1), new Vector3d(1, 1, 1));

        var bbox = new[] { outer, inner }.BoundingBox();

        (bbox.Min - new Vector3d(-5, -5, -5)).Length.Should().BeLessThan(TOLERANCE);
        (bbox.Max - new Vector3d(5, 5, 5)).Length.Should().BeLessThan(TOLERANCE);
    }

    [Fact]
    public void BoundingBox_WorksWithLazyEnumerable()
    {
        // Guards against accidentally enumerating more than once.
        var e1 = MakeElement("a", Vector3d.Zero, new Vector3d(1, 1, 1));
        var e2 = MakeElement("b", new Vector3d(2, 2, 2), new Vector3d(3, 3, 3));

        IEnumerable<BuildingElement> Lazy() { yield return e1; yield return e2; }

        var bbox = Lazy().BoundingBox();

        (bbox.Min - Vector3d.Zero).Length.Should().BeLessThan(TOLERANCE);
        (bbox.Max - new Vector3d(3, 3, 3)).Length.Should().BeLessThan(TOLERANCE);
    }
}
