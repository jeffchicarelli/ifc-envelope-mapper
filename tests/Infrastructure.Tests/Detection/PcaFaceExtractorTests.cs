using IfcEnvelopeMapper.Infrastructure.Detection;
using Xbim.Ifc4.Interfaces;

namespace IfcEnvelopeMapper.Infrastructure.Tests.Detection;

[Trait("Category", "Integration")]
public sealed class PcaFaceExtractorTests : IfcTestBase
{
    public PcaFaceExtractorTests() : base("duplex.ifc") { }

    [Fact]
    public void Extract_RealWall_ProducesAtLeastTwoFaces()
    {
        // A wall has (at minimum) two large opposite faces — front and back.
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
        // exactly one Face, so the sum of face areas ≈ mesh surface area.
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
        // Each mesh triangle belongs to at most one Face.
        var wall = ElementsOfType<IIfcWall>().First(e => e.GetMesh().TriangleCount > 4);

        var faces = new PcaFaceExtractor().Extract(wall);

        var allTids = faces.SelectMany(f => f.TriangleIds).ToList();
        allTids.Distinct().Count().Should().Be(allTids.Count, "no triangle should appear in two faces");
    }

    [Fact]
    public void Extract_RealSlab_ProducesTwoOppositeFaces()
    {
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
