using g4;
using IfcEnvelopeMapper.Domain.Extensions;

namespace IfcEnvelopeMapper.Domain.Tests.Extensions;

public class DMesh3ExtensionsTests
{
    private const double TOLERANCE = 1e-10;

    private static DMesh3 MakeTriangle(Vector3d p0, Vector3d p1, Vector3d p2)
    {
        var mesh = new DMesh3();
        var a = mesh.AppendVertex(p0);
        var b = mesh.AppendVertex(p1);
        var c = mesh.AppendVertex(p2);
        mesh.AppendTriangle(new Index3i(a, b, c));
        return mesh;
    }

    // ───── Merge ─────

    [Fact]
    public void Merge_EmptySequence_ReturnsEmptyMesh()
    {
        var merged = Enumerable.Empty<DMesh3>().Merge();

        merged.VertexCount.Should().Be(0);
        merged.TriangleCount.Should().Be(0);
    }

    [Fact]
    public void Merge_SingleMesh_CountsMatchInput()
    {
        var m = MakeTriangle(
            new Vector3d(0, 0, 0),
            new Vector3d(1, 0, 0),
            new Vector3d(0, 1, 0));

        var merged = new[] { m }.Merge();

        merged.VertexCount.Should().Be(3);
        merged.TriangleCount.Should().Be(1);
    }

    [Fact]
    public void Merge_TwoMeshes_CountsAreSums()
    {
        var m1 = MakeTriangle(
            new Vector3d(0, 0, 0),
            new Vector3d(1, 0, 0),
            new Vector3d(0, 1, 0));
        var m2 = MakeTriangle(
            new Vector3d(10, 0, 0),
            new Vector3d(11, 0, 0),
            new Vector3d(10, 1, 0));

        var merged = new[] { m1, m2 }.Merge();

        merged.VertexCount.Should().Be(6);
        merged.TriangleCount.Should().Be(2);
    }

    [Fact]
    public void Merge_TriangleIndicesAreRemappedToMergedVertexSpace()
    {
        // Second mesh's triangle indices originally reference its own vertices 0..2.
        // After merge they must be offset by 3 so the triangle still points to
        // the correct positions in the merged vertex list.
        var m1 = MakeTriangle(
            new Vector3d(0, 0, 0),
            new Vector3d(1, 0, 0),
            new Vector3d(0, 1, 0));
        var m2 = MakeTriangle(
            new Vector3d(10, 0, 0),
            new Vector3d(11, 0, 0),
            new Vector3d(10, 1, 0));

        var merged = new[] { m1, m2 }.Merge();

        var tri = merged.GetTriangle(1);
        var pa  = merged.GetVertex(tri.a);
        var pb  = merged.GetVertex(tri.b);
        var pc  = merged.GetVertex(tri.c);

        pa.Should().Be(new Vector3d(10, 0, 0));
        pb.Should().Be(new Vector3d(11, 0, 0));
        pc.Should().Be(new Vector3d(10, 1, 0));
    }

    // ───── ExtractTriangles ─────

    [Fact]
    public void ExtractTriangles_EmptyIds_ReturnsEmptyMesh()
    {
        var source = MakeTriangle(Vector3d.Zero, Vector3d.AxisX, Vector3d.AxisY);

        var extracted = source.ExtractTriangles(Array.Empty<int>());

        extracted.VertexCount.Should().Be(0);
        extracted.TriangleCount.Should().Be(0);
    }

    [Fact]
    public void ExtractTriangles_AllValidIds_PreservesTriangleCount()
    {
        var source = BuildTwoTriangleMesh();

        var extracted = source.ExtractTriangles(new[] { 0, 1 });

        extracted.TriangleCount.Should().Be(2);
    }

    [Fact]
    public void ExtractTriangles_DuplicatesVerticesPerTriangle()
    {
        // Two triangles that share an edge (4 unique vertices in source).
        // Extraction must NOT share vertices — each triangle gets its own three.
        var source = BuildTwoTriangleMesh();

        var extracted = source.ExtractTriangles(new[] { 0, 1 });

        extracted.VertexCount.Should().Be(6);  // 3 × 2 triangles, no sharing
    }

    [Fact]
    public void ExtractTriangles_InvalidIdsAreSilentlySkipped()
    {
        var source = BuildTwoTriangleMesh();

        // Mix valid (0, 1) and invalid (42, 99) ids; only the valid ones survive.
        var extracted = source.ExtractTriangles(new[] { 0, 42, 1, 99 });

        extracted.TriangleCount.Should().Be(2);
    }

    // ───── helpers ─────

    private static DMesh3 BuildTwoTriangleMesh()
    {
        // Quad as two triangles sharing an edge:
        //   v0───v1
        //   │ ╲  │
        //   │  ╲ │
        //   v3───v2
        var mesh = new DMesh3();
        var v0 = mesh.AppendVertex(new Vector3d(0, 0, 0));
        var v1 = mesh.AppendVertex(new Vector3d(1, 0, 0));
        var v2 = mesh.AppendVertex(new Vector3d(1, 1, 0));
        var v3 = mesh.AppendVertex(new Vector3d(0, 1, 0));
        mesh.AppendTriangle(new Index3i(v0, v1, v2));
        mesh.AppendTriangle(new Index3i(v0, v2, v3));
        return mesh;
    }
}
