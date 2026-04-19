using g4;
using IfcEnvelopeMapper.Algorithms.Detection;
using IfcEnvelopeMapper.Core.Element;

namespace IfcEnvelopeMapper.Tests.Algorithms.Detection;

public sealed class PcaFaceExtractorTests
{
    // Unit cube mesh: 6 faces x 2 triangles = 12 triangles, outward normals.
    private static BuildingElement MakeCubeElement()
    {
        var gen = new TrivialBox3Generator
        {
            Box = new Box3d(Vector3d.Zero, new Vector3d(0.5, 0.5, 0.5))
        };
        var mesh = gen.Generate().MakeDMesh();

        return new BuildingElement
        {
            GlobalId = "test-cube",
            IfcType  = "IfcWall",
            Mesh     = mesh
        };
    }

    [Fact]
    public void Extract_UnitCube_Returns6Faces()
    {
        // Arrange
        var element   = MakeCubeElement();
        var extractor = new PcaFaceExtractor();

        // Act
        var faces = extractor.Extract(element);

        // Assert
        faces.Should().HaveCount(6);
    }

    [Fact]
    public void Extract_UnitCube_EachFaceHasAreaOne()
    {
        // Arrange
        var element   = MakeCubeElement();
        var extractor = new PcaFaceExtractor();

        // Act
        var faces = extractor.Extract(element);

        // Assert — each face of a 1x1x1 cube has area = 1.0 m²
        foreach (var face in faces)
        {
            face.Area.Should().BeApproximately(1.0, precision: 1e-9);
        }
    }

    [Fact]
    public void Extract_UnitCube_AllFacesReferenceElement()
    {
        // Arrange
        var element   = MakeCubeElement();
        var extractor = new PcaFaceExtractor();

        // Act
        var faces = extractor.Extract(element);

        // Assert
        faces.Should().AllSatisfy(f => f.Element.Should().BeSameAs(element));
    }

    [Fact]
    public void Extract_EmptyMesh_ReturnsEmpty()
    {
        // Arrange
        var element = new BuildingElement
        {
            GlobalId = "empty",
            IfcType  = "IfcWall",
            Mesh     = new DMesh3()
        };
        var extractor = new PcaFaceExtractor();

        // Act
        var faces = extractor.Extract(element);

        // Assert
        faces.Should().BeEmpty();
    }

    [Fact]
    public void Extract_UnitCube_NormalsAreApproximatelyCardinal()
    {
        // Arrange
        var element   = MakeCubeElement();
        var extractor = new PcaFaceExtractor();

        // Act
        var faces = extractor.Extract(element);

        // Assert — the 6 normals must cover all 6 cardinal directions (±x, ±y, ±z)
        var cardinals = new[]
        {
            new Vector3d(1, 0, 0), new Vector3d(-1, 0, 0),
            new Vector3d(0, 1, 0), new Vector3d(0, -1, 0),
            new Vector3d(0, 0, 1), new Vector3d(0, 0, -1)
        };

        foreach (var cardinal in cardinals)
        {
            faces.Should().Contain(f =>
                Math.Abs(Math.Abs(f.Normal.Dot(cardinal)) - 1.0) < 0.01,
                because: $"expected a face with normal ≈ {cardinal}");
        }
    }
}
