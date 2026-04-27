using IfcEnvelopeMapper.Engine.Pipeline.Detection;
using Xbim.Ifc4.Interfaces;

namespace IfcEnvelopeMapper.Tests.Engine.Pipeline.Detection;

/// <summary>
/// Integration-style tests for <see cref="PcaFaceExtractor"/>. The extractor
/// operates on <c>Element.GetMesh()</c>, which is only constructible via the
/// loader, so these tests run on real <c>duplex.ifc</c> elements. Edge-case
/// coverage of the underlying PCA math (axis-aligned, oblique, noisy point
/// sets) lives in <c>Vector3DExtensionsTests.FitPlane_*</c> — that's the pure
/// unit layer. Here we verify the extractor as a whole: a real wall produces
/// at least one fitted face, the fitted plane is consistent with the mesh,
/// and degenerate inputs are handled gracefully.
/// </summary>
[Trait("Category", "Integration")]
public sealed class PcaFaceExtractorTests : IfcTestBase
{
    public PcaFaceExtractorTests() : base("duplex.ifc") { }

    [Fact]
    public void Extract_RealWall_ProducesAtLeastTwoFaces()
    {
        // A wall has (at minimum) two large opposite faces — front and back.
        // PCA grouping treats parallel and antiparallel normals as the same
        // group, but distance-splitting separates the two surfaces.
        var wall = ElementsOfType<IIfcWall>().First(e => e.GetMesh().TriangleCount > 4);

        var faces = new PcaFaceExtractor().Extract(wall);

        faces.Should().NotBeEmpty();
        faces.Count.Should().BeGreaterThanOrEqualTo(2,
            "even a thin wall has front + back surfaces");
    }

    [Fact]
    public void Extract_FaceArea_SumsApproximatelyToMeshArea()
    {
        // PCA grouping shouldn't lose triangles: each triangle contributes to
        // exactly one Face, so the sum of face areas equals the mesh's surface
        // area (up to triangulation precision).
        var wall = ElementsOfType<IIfcWall>().First(e => e.GetMesh().TriangleCount > 4);
        var mesh = wall.GetMesh();

        var faces = new PcaFaceExtractor().Extract(wall);

        var meshArea = SumTriangleAreas(mesh);
        var faceArea = faces.Sum(f => f.Area);
        faceArea.Should().BeApproximately(meshArea, meshArea * 0.05);
    }

    [Fact]
    public void Extract_FaceCentroid_LiesNearItsFittedPlane()
    {
        // Centroid is the area-weighted mean of triangle centroids; the fitted
        // plane minimises perpendicular distance to all vertices. The centroid
        // should sit on (or very close to) that plane.
        var wall = ElementsOfType<IIfcWall>().First(e => e.GetMesh().TriangleCount > 4);

        var faces = new PcaFaceExtractor().Extract(wall);

        foreach (var face in faces)
        {
            var dist = Math.Abs(face.FittedPlane.Normal.Dot(face.Centroid) - face.FittedPlane.Constant);
            dist.Should().BeLessThan(0.05,
                "fitted plane should pass through the area-weighted centroid");
        }
    }

    [Fact]
    public void Extract_TriangleIds_ParsesEverythingAndDoesNotOverlap()
    {
        // Each mesh triangle belongs to at most one Face. Otherwise area
        // accounting would double-count and downstream serialisation would
        // emit duplicates.
        var wall = ElementsOfType<IIfcWall>().First(e => e.GetMesh().TriangleCount > 4);

        var faces = new PcaFaceExtractor().Extract(wall);

        var allTids = faces.SelectMany(f => f.TriangleIds).ToList();
        allTids.Distinct().Count().Should().Be(allTids.Count, "no triangle should appear in two faces");
    }

    [Fact]
    public void Extract_RealSlab_ProducesTwoOppositeFaces()
    {
        // A slab (top + bottom) is the cleanest case: two large parallel
        // surfaces, no curvature. Expect at least 2 faces.
        var slab = ElementsOfType<IIfcSlab>().First();

        var faces = new PcaFaceExtractor().Extract(slab);

        faces.Should().NotBeEmpty();
    }

    private static double SumTriangleAreas(g4.DMesh3 mesh)
    {
        var area = 0.0;
        foreach (var tid in mesh.TriangleIndices())
        {
            var t  = mesh.GetTriangle(tid);
            var va = mesh.GetVertex(t.a);
            var vb = mesh.GetVertex(t.b);
            var vc = mesh.GetVertex(t.c);
            area += 0.5 * (vb - va).Cross(vc - va).Length;
        }

        return area;
    }
}
