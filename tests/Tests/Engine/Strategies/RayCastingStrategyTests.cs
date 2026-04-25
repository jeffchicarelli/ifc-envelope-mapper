using g4;
using IfcEnvelopeMapper.Core.Domain.Element;
using IfcEnvelopeMapper.Engine.Strategies;

namespace IfcEnvelopeMapper.Tests.Engine.Strategies;

public sealed class RayCastingStrategyTests
{
    private static BuildingElement MakeCubeElement(string id, double halfExtent)
    {
        var gen = new TrivialBox3Generator
        {
            Box = new Box3d(Vector3d.Zero, new Vector3d(halfExtent, halfExtent, halfExtent))
        };
        return new BuildingElement
        {
            GlobalId = id,
            IfcType  = "IfcWall",
            Mesh     = gen.Generate().MakeDMesh()
        };
    }

    [Fact]
    public void Detect_EmptyElements_ReturnsEmptyResult()
    {
        // Arrange
        var strategy = new RayCastingStrategy();

        // Act
        var result = strategy.Detect(Array.Empty<BuildingElement>());

        // Assert
        result.Classifications.Should().BeEmpty();
        result.Envelope.Faces.Should().BeEmpty();
    }

    [Fact]
    public void Detect_SingleClosedCube_ClassifiesElementAsExterior()
    {
        // Arrange
        var cube     = MakeCubeElement("cube", halfExtent: 1.0);
        var strategy = new RayCastingStrategy();

        // Act
        var result = strategy.Detect([cube]);

        // Assert
        var classification = result.Classifications.Single(c => c.Element.GlobalId == "cube");
        classification.IsExterior.Should().BeTrue();
    }

    [Fact]
    public void Detect_SingleClosedCube_ProducesSixFaces()
    {
        // Arrange
        var cube     = MakeCubeElement("cube", halfExtent: 1.0);
        var strategy = new RayCastingStrategy();

        // Act
        var result = strategy.Detect([cube]);

        // Assert — PCA extracts one face per cube side
        result.Envelope.Faces.Should().HaveCount(6);
    }

    [Fact]
    public void Detect_OuterCubeEnclosingInnerCube_OnlyOuterIsExterior()
    {
        // Arrange — outer 4×4×4 (half-extent 2), inner 1×1×1 (half-extent 0.5, fully enclosed)
        var outer    = MakeCubeElement("outer", halfExtent: 2.0);
        var inner    = MakeCubeElement("inner", halfExtent: 0.5);
        var strategy = new RayCastingStrategy();

        // Act
        var result = strategy.Detect([outer, inner]);

        // Assert
        var outerClass = result.Classifications.Single(c => c.Element.GlobalId == "outer");
        var innerClass = result.Classifications.Single(c => c.Element.GlobalId == "inner");
        outerClass.IsExterior.Should().BeTrue();
        innerClass.IsExterior.Should().BeFalse();
    }

    [Fact]
    public void Detect_ElementWithNoTriangles_IsClassifiedAsNotExterior()
    {
        // Arrange — outer populates the BVH; the empty-mesh element has no rays to cast
        var outer = MakeCubeElement("outer", halfExtent: 2.0);
        var noMesh = new BuildingElement
        {
            GlobalId = "no-mesh",
            IfcType  = "IfcWall",
            Mesh     = new DMesh3()
        };
        var strategy = new RayCastingStrategy();

        // Act
        var result = strategy.Detect([outer, noMesh]);

        // Assert
        var noMeshClass = result.Classifications.Single(c => c.Element.GlobalId == "no-mesh");
        noMeshClass.IsExterior.Should().BeFalse();
    }

    [Fact]
    public void Detect_SameInputTwice_ProducesIdenticalClassifications()
    {
        // Arrange — two independent element pairs with identical geometry
        var pair1 = new[]
        {
            MakeCubeElement("outer", halfExtent: 2.0),
            MakeCubeElement("inner", halfExtent: 0.5),
        };
        var pair2 = new[]
        {
            MakeCubeElement("outer", halfExtent: 2.0),
            MakeCubeElement("inner", halfExtent: 0.5),
        };
        var strategy = new RayCastingStrategy();

        // Act
        var first  = strategy.Detect(pair1);
        var second = strategy.Detect(pair2);

        // Assert — seeded Random(42) per Detect call ⇒ identical decisions
        var firstFlags  = first.Classifications.OrderBy(c => c.Element.GlobalId, StringComparer.Ordinal)
                                               .Select(c => c.IsExterior).ToList();
        var secondFlags = second.Classifications.OrderBy(c => c.Element.GlobalId, StringComparer.Ordinal)
                                                .Select(c => c.IsExterior).ToList();
        firstFlags.Should().Equal(secondFlags);
    }
}
