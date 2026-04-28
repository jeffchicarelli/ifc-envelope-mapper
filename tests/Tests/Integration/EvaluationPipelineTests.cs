using IfcEnvelopeMapper.Engine.Pipeline.Evaluation;
using IfcEnvelopeMapper.Engine.Pipeline.Detection;
using IfcEnvelopeMapper.Ifc.Loading;


#if DEBUG
#endif

namespace IfcEnvelopeMapper.Tests.Integration;

// End-to-end check of EvaluationPipeline against the bundled duplex.ifc model.
// Catches accidental drift in the voxel flood-fill detection (TP/FP/FN/TN) and
// emits a per-test GLB that colour-codes every element by classification result
// — open `Path.GetTempPath() + EvaluationPipelineTests_disagreement.glb` in any
// glTF viewer to see exactly which elements moved categories. Debug-config-only.
[Trait("Category", "Integration")]
public sealed class EvaluationPipelineTests : IfcTestBase
{
    // Golden counts captured from a clean CLI run on duplex.ifc + voxelSize=0.25.
    // Values shifted in P4.1 (Element with lazy mesh, bbox sourced from
    // XbimShapeInstance.BoundingBox instead of Mesh.GetBounds()); +/- TOLERANCE
    // absorbs precision-related drift on elements near the voxel grid boundary.
    // If the algorithm changes substantially, update these and re-baseline.
    private const int EXPECTED_TP = 42;
    private const int EXPECTED_FP = 8;
    private const int EXPECTED_FN = 4;
    private const int EXPECTED_TN = 70;
    private const int TOLERANCE   = 5;

    // Loose floors for the second test — drift below these means the algorithm
    // got materially worse, regardless of which specific elements changed.
    private const double PRECISION_FLOOR = 0.80;  // currently ~0.849
    private const double RECALL_FLOOR    = 0.85;  // currently ~0.918

    private const double VOXEL_SIZE = 0.25;

    public EvaluationPipelineTests() : base("duplex.ifc") { }

    [Fact]
    public void Pipeline_OnDuplex_ProducesExpectedCounts()
    {
        // Arrange
        var gtPath = GroundTruthPath("duplex.csv");
        var strategy = new VoxelFloodFillStrategy(voxelSize: VOXEL_SIZE);

        // Act
        var result = EvaluationPipeline.EvaluateDetection(IfcPath, gtPath, strategy, new XbimModelLoader());

        // Assert
        result.Counts.TruePositives.Should().BeInRange(EXPECTED_TP - TOLERANCE, EXPECTED_TP + TOLERANCE);
        result.Counts.FalsePositives.Should().BeInRange(EXPECTED_FP - TOLERANCE, EXPECTED_FP + TOLERANCE);
        result.Counts.FalseNegatives.Should().BeInRange(EXPECTED_FN - TOLERANCE, EXPECTED_FN + TOLERANCE);
        result.Counts.TrueNegatives.Should().BeInRange(EXPECTED_TN - TOLERANCE, EXPECTED_TN + TOLERANCE);
    }

    [Fact]
    public void Pipeline_OnDuplex_PrecisionRecallAboveFloor()
    {
        // Arrange
        var gtPath = GroundTruthPath("duplex.csv");
        var strategy = new VoxelFloodFillStrategy(voxelSize: VOXEL_SIZE);

        // Act
        var result = EvaluationPipeline.EvaluateDetection(IfcPath, gtPath, strategy, new XbimModelLoader());

        // Assert
        result.Counts.Precision.Should().BeGreaterThanOrEqualTo(PRECISION_FLOOR);
        result.Counts.Recall.Should().BeGreaterThanOrEqualTo(RECALL_FLOOR);
    }
}
