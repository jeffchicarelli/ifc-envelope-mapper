using FluentAssertions.Execution;
using IfcEnvelopeMapper.Engine.Pipeline.Evaluation;
using IfcEnvelopeMapper.Engine.Pipeline.Detection;
using IfcEnvelopeMapper.Tests.Fixtures;

#if DEBUG
using IfcEnvelopeMapper.Engine.Debug;
#endif

namespace IfcEnvelopeMapper.Tests.Integration;

// End-to-end check of EvaluationPipeline against the bundled duplex.ifc model
// using RayCastingStrategy. Catches drift in the per-triangle ray casting
// classifier (TP/FP/FN/TN) and emits a per-test GLB colour-coded by classification
// vs ground truth — open `Path.GetTempPath() + RayCastingEvaluationTests_<name>.glb`
// in any glTF viewer to see exactly which elements moved categories.
[Trait("Category", "Integration")]
public sealed class RayCastingEvaluationTests : IClassFixture<IfcModelFixture>
{
    // Golden counts captured from a clean run on duplex.ifc with RayCastingStrategy
    // defaults (numRays=8, jitterDeg=5°, hitRatio=0.5, Random seed=42).
    // If the algorithm changes intentionally, update these and re-baseline. If
    // they change unintentionally, the disagreement GLB tells you what moved.
    private const int EXPECTED_TP = 46;
    private const int EXPECTED_FP = 35;
    private const int EXPECTED_FN = 3;
    private const int EXPECTED_TN = 43;

    // Loose floors — drift below these means the algorithm got materially worse.
    // Currently P≈0.568, R≈0.939. Precision is intentionally low: ray casting
    // over-classifies as exterior (the expected tradeoff vs voxel).
    private const double PRECISION_FLOOR = 0.50;
    private const double RECALL_FLOOR    = 0.90;

    private readonly IfcModelFixture _fixture;

    public RayCastingEvaluationTests(IfcModelFixture fixture) => _fixture = fixture;

    [Fact]
    public void Pipeline_OnDuplex_RayCasting_ProducesExpectedCounts()
    {
        // Arrange
        var gtPath   = ResolveGroundTruthPath();
        var strategy = new RayCastingStrategy();

        // Act
        var result = EvaluationPipeline.EvaluateDetection(_fixture.IfcPath, gtPath, strategy);
        EmitDisagreementGlb(result, nameof(Pipeline_OnDuplex_RayCasting_ProducesExpectedCounts));

        // Assert — AssertionScope reports all four mismatches in one error so
        // baselining only takes one test run.
        using var scope = new AssertionScope();
        result.Counts.TruePositives.Should().Be(EXPECTED_TP);
        result.Counts.FalsePositives.Should().Be(EXPECTED_FP);
        result.Counts.FalseNegatives.Should().Be(EXPECTED_FN);
        result.Counts.TrueNegatives.Should().Be(EXPECTED_TN);
    }

    [Fact]
    public void Pipeline_OnDuplex_RayCasting_PrecisionRecallAboveFloor()
    {
        // Arrange
        var gtPath   = ResolveGroundTruthPath();
        var strategy = new RayCastingStrategy();

        // Act
        var result = EvaluationPipeline.EvaluateDetection(_fixture.IfcPath, gtPath, strategy);

        // Assert
        result.Counts.Precision.Should().BeGreaterThanOrEqualTo(PRECISION_FLOOR);
        result.Counts.Recall.Should().BeGreaterThanOrEqualTo(RECALL_FLOOR);
    }

    private string ResolveGroundTruthPath()
    {
        // ifcPath = .../data/models/duplex.ifc → gtPath = .../data/ground-truth/duplex.csv
        var dataDir = Path.GetDirectoryName(Path.GetDirectoryName(_fixture.IfcPath))!;
        return Path.Combine(dataDir, "ground-truth", "duplex.csv");
    }

    private static void EmitDisagreementGlb(EvaluationResult result, string testName)
    {
#if DEBUG
        var glbPath = Path.Combine(Path.GetTempPath(), $"RayCastingEvaluationTests_{testName}.glb");
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
            GeometryDebug.Element(c.Element.GetMesh(), c.Element.GlobalId, c.Element.IfcType, color);
        }
#endif
    }
}
