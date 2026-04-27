using FluentAssertions.Execution;
using IfcEnvelopeMapper.Engine.Pipeline.Evaluation;
using IfcEnvelopeMapper.Engine.Strategies;
using IfcEnvelopeMapper.Ifc.Evaluation;
using IfcEnvelopeMapper.Tests.Fixtures;

#if DEBUG
using IfcEnvelopeMapper.Engine.Visualization;
#endif

namespace IfcEnvelopeMapper.Tests.Integration;

// End-to-end check of EvaluationPipeline against demo2.ifc with RayCastingStrategy.
// Provides a second real-world data point — demonstrates that ray casting
// generalises beyond duplex.ifc.
//
// VoxelFloodFillStrategy is NOT exercised on demo2: the model carries geometry
// at very large world coordinates (likely a georeferenced site origin), so the
// bounding-box-driven VoxelGrid3D allocation overflows .NET's Array.MaxLength
// regardless of voxel size. Voxel coverage on a second fixture happens on the
// degraded fixture (P3.1 step 3) instead.
//
// Ground truth (data/ground-truth/demo2.csv) is auto-generated from demo2.ifc
// IsExternal psets on first run by GroundTruthGenerator, then committed.
[Trait("Category", "Integration")]
public sealed class Demo2EvaluationTests : IClassFixture<Demo2ModelFixture>
{
    // RayCasting golden counts on demo2 with defaults (numRays=8, jitterDeg=5°,
    // hitRatio=0.5, Random seed=42). 97 ground-truth records evaluable
    // (3 elements have IsExternal=null and are excluded).
    private const int RAYCAST_TP = 64;
    private const int RAYCAST_FP = 25;
    private const int RAYCAST_FN = 3;
    private const int RAYCAST_TN = 5;

    // Currently P≈0.719, R≈0.955.
    private const double RAYCAST_PRECISION_FLOOR = 0.65;
    private const double RAYCAST_RECALL_FLOOR    = 0.90;

    private readonly Demo2ModelFixture _fixture;

    public Demo2EvaluationTests(Demo2ModelFixture fixture) => _fixture = fixture;

    [Fact]
    public void Pipeline_OnDemo2_RayCasting_ProducesExpectedCounts()
    {
        // Arrange
        var gtPath   = TestPaths.GroundTruthPath("demo2.csv");
        var strategy = new RayCastingStrategy();

        // Act
        var result = EvaluationPipeline.EvaluateDetection(_fixture.IfcPath, gtPath, strategy);
        EmitDisagreementGlb(result, nameof(Pipeline_OnDemo2_RayCasting_ProducesExpectedCounts));

        // Assert
        using var scope = new AssertionScope();
        result.Counts.TruePositives.Should().Be(RAYCAST_TP);
        result.Counts.FalsePositives.Should().Be(RAYCAST_FP);
        result.Counts.FalseNegatives.Should().Be(RAYCAST_FN);
        result.Counts.TrueNegatives.Should().Be(RAYCAST_TN);
    }

    [Fact]
    public void Pipeline_OnDemo2_RayCasting_PrecisionRecallAboveFloor()
    {
        // Arrange
        var gtPath   = TestPaths.GroundTruthPath("demo2.csv");
        var strategy = new RayCastingStrategy();

        // Act
        var result = EvaluationPipeline.EvaluateDetection(_fixture.IfcPath, gtPath, strategy);

        // Assert
        result.Counts.Precision.Should().BeGreaterThanOrEqualTo(RAYCAST_PRECISION_FLOOR);
        result.Counts.Recall.Should().BeGreaterThanOrEqualTo(RAYCAST_RECALL_FLOOR);
    }

    private static void EmitDisagreementGlb(EvaluationResult result, string testName)
    {
#if DEBUG
        var glbPath = Path.Combine(Path.GetTempPath(), $"Demo2EvaluationTests_{testName}.glb");
        GeometryDebug.Configure(glbPath, launchServer: false);
        GeometryDebug.Clear();

        var gtByGid = result.GroundTruth
            .Where(g => g.IsExterior.HasValue)
            .ToDictionary(g => g.GlobalId, g => g.IsExterior!.Value, StringComparer.Ordinal);

        foreach (var c in result.Detection.Classifications)
        {
            if (!gtByGid.TryGetValue(c.Element.GlobalId, out var gt))
            {
                continue;
            }

            // TP=green, TN=gray, FP=red (claimed exterior, isn't), FN=orange (missed exterior).
            var color = (c.IsExterior, gt) switch
            {
                (true,  true)  => "#00ff00",
                (false, false) => "#888888",
                (true,  false) => "#ff0000",
                (false, true)  => "#ff8800",
            };
            GeometryDebug.Element(c.Element.Mesh, c.Element.GlobalId, c.Element.IfcType, color);
        }
#endif
    }
}
