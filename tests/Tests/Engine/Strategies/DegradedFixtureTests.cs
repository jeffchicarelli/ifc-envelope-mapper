using g4;
using IfcEnvelopeMapper.Core.Domain.Element;
using IfcEnvelopeMapper.Engine.Strategies;

namespace IfcEnvelopeMapper.Tests.Engine.Strategies;

// Synthetic-geometry contrast between VoxelFloodFillStrategy and
// RayCastingStrategy on a controlled fixture: a 6-wall cube enclosure
// with a "table" element inside, with and without a missing wall ("gap").
//
//     closed enclosure                    gapped enclosure (+X wall removed)
//     ┌───────────────┐                   ┌───────────────
//     │               │                   │
//     │     table     │                   │     table
//     │               │                   │
//     └───────────────┘                   └───────────────
//
// The closed-enclosure cases are baselines — both strategies should classify
// the table as interior. The gapped cases document each strategy's failure
// mode: voxel flood-fill leaks globally (table reached from outside through
// the gap), while ray casting fails locally on the +X face of the table
// (rays from that face escape through the gap; element-level "any exterior
// triangle ⇒ exterior" decision flips the table).
public sealed class DegradedFixtureTests
{
    private const double ENCLOSURE_HALF_SIZE = 2.0;   // outer cube extends ±2 m
    private const double WALL_HALF_THICKNESS = 0.05;  // 0.10 m wall thickness
    private const double TABLE_HALF_EXTENT   = 0.5;   // 1×1×1 m table at origin

    [Fact]
    public void ClosedEnclosure_Voxel_TableIsInterior()
    {
        // Arrange
        var elements = BuildSixWallEnclosure(withGap: false);
        var strategy = new VoxelFloodFillStrategy(voxelSize: 0.25);

        // Act
        var result = strategy.Detect(elements);

        // Assert
        var table = result.Classifications.Single(c => c.Element.GlobalId == "table");
        table.IsExterior.Should().BeFalse();
    }

    [Fact]
    public void ClosedEnclosure_RayCasting_TableIsInterior()
    {
        // Arrange
        var elements = BuildSixWallEnclosure(withGap: false);
        var strategy = new RayCastingStrategy();

        // Act
        var result = strategy.Detect(elements);

        // Assert
        var table = result.Classifications.Single(c => c.Element.GlobalId == "table");
        table.IsExterior.Should().BeFalse();
    }

    [Fact]
    public void GappedEnclosure_Voxel_TableLeaksAsExterior()
    {
        // Arrange — +X wall missing; the exterior flood-fill seeps through
        // the opening and reaches the table from outside.
        var elements = BuildSixWallEnclosure(withGap: true);
        var strategy = new VoxelFloodFillStrategy(voxelSize: 0.25);

        // Act
        var result = strategy.Detect(elements);

        // Assert — voxel is fooled by the gap.
        var table = result.Classifications.Single(c => c.Element.GlobalId == "table");
        table.IsExterior.Should().BeTrue();
    }

    [Fact]
    public void GappedEnclosure_RayCasting_TableExteriorViaGapFace()
    {
        // Arrange — same gap geometry. Rays from the table's +X face point
        // toward the missing wall and escape; rays from the other 5 faces
        // hit walls that are still present.
        var elements = BuildSixWallEnclosure(withGap: true);
        var strategy = new RayCastingStrategy();

        // Act
        var result = strategy.Detect(elements);

        // Assert — element-level decision is "any exterior triangle ⇒ exterior",
        // so the +X-face triangles flip the table even though the other 5 faces
        // are correctly inside. A smaller / off-axis gap would let raycast still
        // classify the table interior; that variant is left for follow-up.
        var table = result.Classifications.Single(c => c.Element.GlobalId == "table");
        table.IsExterior.Should().BeTrue();
    }

    // Builds an axis-aligned 4×4×4 m enclosure of 6 thin walls at the origin
    // plus a 1×1×1 m "table" element at the centre. When <paramref name="withGap"/>
    // is true the +X wall is omitted, leaving an opening on that side.
    private static List<BuildingElement> BuildSixWallEnclosure(bool withGap)
    {
        var elements = new List<BuildingElement>
        {
            // -X wall
            MakeBox("wall_neg_x",
                center:      new Vector3d(-ENCLOSURE_HALF_SIZE, 0, 0),
                halfExtents: new Vector3d(WALL_HALF_THICKNESS, ENCLOSURE_HALF_SIZE, ENCLOSURE_HALF_SIZE)),
            // -Y / +Y walls
            MakeBox("wall_neg_y",
                center:      new Vector3d(0, -ENCLOSURE_HALF_SIZE, 0),
                halfExtents: new Vector3d(ENCLOSURE_HALF_SIZE, WALL_HALF_THICKNESS, ENCLOSURE_HALF_SIZE)),
            MakeBox("wall_pos_y",
                center:      new Vector3d(0, ENCLOSURE_HALF_SIZE, 0),
                halfExtents: new Vector3d(ENCLOSURE_HALF_SIZE, WALL_HALF_THICKNESS, ENCLOSURE_HALF_SIZE)),
            // -Z / +Z walls (floor + ceiling)
            MakeBox("wall_neg_z",
                center:      new Vector3d(0, 0, -ENCLOSURE_HALF_SIZE),
                halfExtents: new Vector3d(ENCLOSURE_HALF_SIZE, ENCLOSURE_HALF_SIZE, WALL_HALF_THICKNESS)),
            MakeBox("wall_pos_z",
                center:      new Vector3d(0, 0, ENCLOSURE_HALF_SIZE),
                halfExtents: new Vector3d(ENCLOSURE_HALF_SIZE, ENCLOSURE_HALF_SIZE, WALL_HALF_THICKNESS)),
            // Table at the centre
            MakeBox("table",
                center:      Vector3d.Zero,
                halfExtents: new Vector3d(TABLE_HALF_EXTENT, TABLE_HALF_EXTENT, TABLE_HALF_EXTENT)),
        };

        if (!withGap)
        {
            elements.Insert(1, MakeBox("wall_pos_x",
                center:      new Vector3d(ENCLOSURE_HALF_SIZE, 0, 0),
                halfExtents: new Vector3d(WALL_HALF_THICKNESS, ENCLOSURE_HALF_SIZE, ENCLOSURE_HALF_SIZE)));
        }

        return elements;
    }

    private static BuildingElement MakeBox(string id, Vector3d center, Vector3d halfExtents)
    {
        var gen = new TrivialBox3Generator { Box = new Box3d(center, halfExtents) };
        return new BuildingElement
        {
            GlobalId = id,
            IfcType  = id == "table" ? "IfcFurnishingElement" : "IfcWall",
            Mesh     = gen.Generate().MakeDMesh(),
        };
    }
}
