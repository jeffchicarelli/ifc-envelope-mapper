using g4;
using IfcEnvelopeMapper.Engine.Pipeline.Detection;
using IfcEnvelopeMapper.Engine.Visualization.Api;
using IfcEnvelopeMapper.Ifc.Domain.Surface;

namespace IfcEnvelopeMapper.Tests.Engine.Pipeline.Detection;

// ──────────────────────────────────────────────────────────────────────────────
// Pure unit tests — no IFC file, no Element stubs required.
// Only the empty-envelope fast-path can be exercised here because Face requires
// a real Element (xBIM IIfcProduct-backed), so algorithm tests on non-trivial
// inputs live in DbscanFacadeGrouperIntegrationTests below.
// ──────────────────────────────────────────────────────────────────────────────

public sealed class DbscanFacadeGrouperTests
{
    [Fact]
    public void Group_EmptyEnvelope_ReturnsEmpty()
    {
        var envelope = new Envelope(new DMesh3(), []);
        var facades  = new DbscanFacadeGrouper().Group(envelope);
        facades.Should().BeEmpty();
    }
}

// ──────────────────────────────────────────────────────────────────────────────
// Integration tests — real faces from duplex.ifc via VoxelFloodFillStrategy.
//
// Why VoxelFloodFillStrategy and not RayCasting?
//   Voxel produces a richer Envelope.Faces (more coplanar groups, populated by
//   PcaFaceExtractor) than RayCasting (which classifies per-triangle and hands
//   a raw triangle list to PcaFaceExtractor). Either would work, but the voxel
//   path is the canonical Stage 1 for facade grouping.
//
// Covered properties:
//   • Partition: every face appears in exactly one Facade
//   • Unique IDs: "facade-NN" is never repeated
//   • At least 4 facades: duplex has N/E/S/W cardinal walls
//   • Azimuth range: all values in [0, 360)
//   • Ordered: returned list is sorted ascending by AzimuthDegrees
//   • Dominant normal: each facade's area-weighted normal is a unit vector
//   • Envelope back-reference: each Facade.Envelope points to the same object
// ──────────────────────────────────────────────────────────────────────────────

[Trait("Category", "Integration")]
public sealed class DbscanFacadeGrouperIntegrationTests : IfcTestBase
{
    public DbscanFacadeGrouperIntegrationTests() : base("duplex.ifc")
    {
        // Scene uses AsyncLocal<State>: the IfcTestBase static ctor configures it
        // in the static-initializer execution context, which does NOT propagate to
        // each test method's execution context. Calling Configure here ensures the
        // current test instance's context has a valid Scene — same pattern the
        // RayCastingEvaluationTests / Demo2EvaluationTests use for EmitDisagreementGlb.
        GeometryDebug.Configure(
            Path.Combine(Path.GetTempPath(), "DbscanFacadeGrouperTests.glb"),
            launchServer: false);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private IReadOnlyList<Facade> RunGrouper(
        double epsilonDeg = 15.0, int minFaces = 3, double adjacencyM = 3.0)
    {
        var result = new VoxelFloodFillStrategy().Detect(Model.Elements);
        return new DbscanFacadeGrouper(epsilonDeg, minFaces, adjacencyM)
                   .Group(result.Envelope);
    }

    private Envelope RunEnvelope()
        => new VoxelFloodFillStrategy().Detect(Model.Elements).Envelope;

    // ── correctness ───────────────────────────────────────────────────────────

    [Fact]
    public void Group_OnDuplex_ProducesAtLeastFourFacades()
    {
        // duplex has at minimum N/E/S/W cardinal facades
        RunGrouper().Should().HaveCountGreaterThanOrEqualTo(4);
    }

    [Fact]
    public void Group_OnDuplex_FacesArePartitioned()
    {
        // every Face from the envelope belongs to exactly one Facade (no overlap)
        var facades    = RunGrouper();
        var allFaces   = facades.SelectMany(f => f.Faces).ToList();
        allFaces.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Group_OnDuplex_EachFacadeHasUniqueId()
    {
        var facades = RunGrouper();
        facades.Select(f => f.Id).Should().OnlyHaveUniqueItems();
    }

    // ── azimuth ───────────────────────────────────────────────────────────────

    [Fact]
    public void Group_OnDuplex_AzimuthsInRange()
    {
        var facades = RunGrouper();
        facades.Should().AllSatisfy(f =>
        {
            f.AzimuthDegrees.Should().BeGreaterThanOrEqualTo(0.0);
            f.AzimuthDegrees.Should().BeLessThan(360.0);
        });
    }

    [Fact]
    public void Group_OnDuplex_FacadesOrderedByAzimuth()
    {
        // Group() sorts ascending so the caller gets a stable N→E→S→W sequence
        var facades  = RunGrouper();
        var azimuths = facades.Select(f => f.AzimuthDegrees).ToList();
        azimuths.Should().BeInAscendingOrder();
    }

    // ── dominant normal ───────────────────────────────────────────────────────

    [Fact]
    public void Group_OnDuplex_DominantNormalsAreUnit()
    {
        var facades = RunGrouper();
        facades.Should().AllSatisfy(f =>
            f.DominantNormal.Length.Should().BeApproximately(1.0, precision: 1e-6));
    }

    // ── structural invariants ─────────────────────────────────────────────────

    [Fact]
    public void Group_OnDuplex_EnvelopeBackReferenceIsPreserved()
    {
        // Facade.Envelope must point to the same Envelope used in the Group() call
        var envelope = RunEnvelope();
        var facades  = new DbscanFacadeGrouper().Group(envelope);
        facades.Should().AllSatisfy(f => f.Envelope.Should().BeSameAs(envelope));
    }
}
