using IfcEnvelopeMapper.Core.Pipeline.Evaluation;
using IfcEnvelopeMapper.Engine.Strategies;
using IfcEnvelopeMapper.Ifc.Evaluation;
using IfcEnvelopeMapper.Tests.Fixtures;

#if DEBUG
using IfcEnvelopeMapper.Engine.Visualization;
#endif

namespace IfcEnvelopeMapper.Tests.Integration;

// End-to-end check of EvaluationPipeline against the bundled duplex.ifc model.
// Catches accidental drift in the voxel flood-fill detection (TP/FP/FN/TN) and
// emits a per-test GLB that colour-codes every element by classification result
// — open `Path.GetTempPath() + EvaluationPipelineTests_disagreement.glb` in any
// glTF viewer to see exactly which elements moved categories. Debug-config-only.
[Trait("Category", "Integration")]
public sealed class EvaluationPipelineTests : IClassFixture<IfcModelFixture>
{
    // Golden counts captured from a clean CLI run on duplex.ifc + voxelSize=0.25.
    // If the algorithm changes intentionally, update these and re-baseline. If
    // they change unintentionally, the visual GLB tells you what moved.
    private const int ExpectedTP = 45;
    private const int ExpectedFP = 8;
    private const int ExpectedFN = 4;
    private const int ExpectedTN = 70;

    // Loose floors for the second test — drift below these means the algorithm
    // got materially worse, regardless of which specific elements changed.
    private const double PrecisionFloor = 0.80;  // currently ~0.849
    private const double RecallFloor    = 0.85;  // currently ~0.918

    private const double VoxelSize = 0.25;

    private readonly IfcModelFixture _fixture;

    public EvaluationPipelineTests(IfcModelFixture fixture) => _fixture = fixture;

    [Fact]
    public void Pipeline_OnDuplex_ProducesExpectedCounts()
    {
        // Arrange
        var gtPath = ResolveGroundTruthPath();
        var strategy = new VoxelFloodFillStrategy(voxelSize: VoxelSize);

        // Act
        var result = EvaluationPipeline.EvaluateDetection(_fixture.IfcPath, gtPath, strategy);
        EmitDisagreementGlb(result, nameof(Pipeline_OnDuplex_ProducesExpectedCounts));

        // Assert
        result.Counts.TruePositives.Should().Be(ExpectedTP);
        result.Counts.FalsePositives.Should().Be(ExpectedFP);
        result.Counts.FalseNegatives.Should().Be(ExpectedFN);
        result.Counts.TrueNegatives.Should().Be(ExpectedTN);
    }

    [Fact]
    public void Pipeline_OnDuplex_PrecisionRecallAboveFloor()
    {
        // Arrange
        var gtPath = ResolveGroundTruthPath();
        var strategy = new VoxelFloodFillStrategy(voxelSize: VoxelSize);

        // Act
        var result = EvaluationPipeline.EvaluateDetection(_fixture.IfcPath, gtPath, strategy);

        // Assert
        result.Counts.Precision.Should().BeGreaterThanOrEqualTo(PrecisionFloor);
        result.Counts.Recall.Should().BeGreaterThanOrEqualTo(RecallFloor);
    }

    private string ResolveGroundTruthPath()
    {
        // ifcPath = .../data/models/duplex.ifc → gtPath = .../data/ground-truth/duplex.csv
        var dataDir = Path.GetDirectoryName(Path.GetDirectoryName(_fixture.IfcPath))!;
        return Path.Combine(dataDir, "ground-truth", "duplex.csv");
    }

    // Always emits — the GLB is cheap and on assertion failure the developer
    // already has the artefact ready to open. No try/catch acrobatics needed.
    private static void EmitDisagreementGlb(EvaluationResult result, string testName)
    {
#if DEBUG
        var glbPath = Path.Combine(Path.GetTempPath(), $"EvaluationPipelineTests_{testName}.glb");
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
