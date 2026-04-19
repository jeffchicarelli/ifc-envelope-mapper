using g4;
using IfcEnvelopeMapper.Algorithms.Detection;
using IfcEnvelopeMapper.Core.Element;

namespace IfcEnvelopeMapper.Tests.Algorithms.Detection;

public sealed class VoxelFloodFillStrategyTests
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
        var strategy = new VoxelFloodFillStrategy(voxelSize: 0.5);

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
        var outer    = MakeCubeElement("outer", halfExtent: 1.0);
        var strategy = new VoxelFloodFillStrategy(voxelSize: 0.5);

        // Act
        var result = strategy.Detect([outer]);

        // Assert
        var classification = result.Classifications.Single(c => c.Element.GlobalId == "outer");
        classification.IsExterior.Should().BeTrue();
    }

    [Fact]
    public void Detect_OuterCubeEnclosingInnerCube_OnlyOuterIsExterior()
    {
        // Arrange — outer cube 4×4×4 (half-extent 2), inner cube 1×1×1 (half-extent 0.5, fully enclosed)
        var outer    = MakeCubeElement("outer", halfExtent: 2.0);
        var inner    = MakeCubeElement("inner", halfExtent: 0.5);
        var strategy = new VoxelFloodFillStrategy(voxelSize: 0.5);

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
        // Arrange — outer provides a valid grid bbox; no-mesh element has no Occupied voxels
        var outer    = MakeCubeElement("outer", halfExtent: 2.0);
        var noMesh   = new BuildingElement
        {
            GlobalId = "no-mesh",
            IfcType  = "IfcWall",
            Mesh     = new DMesh3()
        };
        var strategy = new VoxelFloodFillStrategy(voxelSize: 0.5);

        // Act
        var result = strategy.Detect([outer, noMesh]);

        // Assert
        var noMeshClass = result.Classifications.Single(c => c.Element.GlobalId == "no-mesh");
        noMeshClass.IsExterior.Should().BeFalse();
    }

    [Fact]
    public void Detect_SmallerVoxelSize_SameClassification()
    {
        // Arrange — same geometry, two voxel resolutions
        var outerCoarse = MakeCubeElement("outer", halfExtent: 2.0);
        var innerCoarse = MakeCubeElement("inner", halfExtent: 0.5);
        var outerFine   = MakeCubeElement("outer", halfExtent: 2.0);
        var innerFine   = MakeCubeElement("inner", halfExtent: 0.5);

        var coarse = new VoxelFloodFillStrategy(voxelSize: 0.5);
        var fine   = new VoxelFloodFillStrategy(voxelSize: 0.25);

        // Act
        var resultCoarse = coarse.Detect([outerCoarse, innerCoarse]);
        var resultFine   = fine.Detect([outerFine, innerFine]);

        // Assert
        var outerCoarseClass = resultCoarse.Classifications.Single(c => c.Element.GlobalId == "outer");
        var innerCoarseClass = resultCoarse.Classifications.Single(c => c.Element.GlobalId == "inner");
        var outerFineClass   = resultFine.Classifications.Single(c => c.Element.GlobalId == "outer");
        var innerFineClass   = resultFine.Classifications.Single(c => c.Element.GlobalId == "inner");

        outerCoarseClass.IsExterior.Should().Be(outerFineClass.IsExterior);
        innerCoarseClass.IsExterior.Should().Be(innerFineClass.IsExterior);
    }
}
