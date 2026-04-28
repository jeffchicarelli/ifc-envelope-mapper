using IfcEnvelopeMapper.Domain.Detection;
using IfcEnvelopeMapper.Infrastructure.Detection;

namespace IfcEnvelopeMapper.Integration.Tests;

/// <summary>
/// Geometric identity tests for the airwell and recess model family. Each test encodes a known geometric truth about a specific element in a
/// specific model — no ground-truth CSV, no confusion matrix. The central invariant under test: An airwell (vertically open shaft) is exterior
/// space. Walls that bound an airwell must therefore be classified exterior, even though they face inward from the building's perspective. If either
/// strategy misclassifies them, flood-fill connectivity or ray escape is broken. Models used (all small, purpose-built, in data/models/airwell/):
/// <list type="bullet">
/// <item>control       — simple box with one square airwell shaft</item> <item>big recess    — L-plan with large exterior recesses</item>
/// <item>small recess  — same geometry, narrower recess</item> <item>with one balcony — L-plan with cantilevered balcony volume</item>
/// <item>round         — box with circular airwell, curved wall mesh</item>
/// </list>
/// </summary>
[Trait("Category", "Integration")]
public sealed class AirwellDetectionTests : IfcTestBase
{
    // ── VoxelFloodFill — unambiguous exterior elements ──────────────────────

    [Theory]
    [InlineData("airwell/big recess.ifc",       "0mEhezLTj6DBaCuBWyJo8X")]
    [InlineData("airwell/big recess.ifc",       "0mEhezLTj6DBaCuBWyJo8I")]
    [InlineData("airwell/big recess.ifc",       "0mEhezLTj6DBaCuBWyJo2A")]
    [InlineData("airwell/big recess.ifc",       "0mEhezLTj6DBaCuBWyJo8d")]
    [InlineData("airwell/with one balcony.ifc", "1wPpdLZdX3efKBKkOInIeM")]
    [InlineData("airwell/with one balcony.ifc", "1wPpdLZdX3efKBKkOInIfD")]
    [InlineData("airwell/with one balcony.ifc", "1wPpdLZdX3efKBKkOInIeg")]
    [InlineData("airwell/with one balcony.ifc", "1wPpdLZdX3efKBKkOInIef")]
    [InlineData("airwell/small recess.ifc",     "0mEhezLTj6DBaCuBWyJoPN")]
    [InlineData("airwell/small recess.ifc",     "0mEhezLTj6DBaCuBWyJoXC")]
    [InlineData("airwell/small recess.ifc",     "0mEhezLTj6DBaCuBWyJoeH")]
    [InlineData("airwell/small recess.ifc",     "0mEhezLTj6DBaCuBWyJoPD")]
    [InlineData("airwell/round.ifc",            "1wPpdLZdX3efKBKkOInJ4E")]
    [InlineData("airwell/round.ifc",            "1wPpdLZdX3efKBKkOInJBz")]
    [InlineData("airwell/round.ifc",            "1wPpdLZdX3efKBKkOInJ4N")]
    [InlineData("airwell/round.ifc",            "1wPpdLZdX3efKBKkOInJ6m")]
    [InlineData("airwell/control.ifc",          "2yrIoyurn0lPXgxy2$CGm7")]
    [InlineData("airwell/control.ifc",          "2yrIoyurn0lPXgxy2$CN7I")]
    [InlineData("airwell/control.ifc",          "2yrIoyurn0lPXgxy2$CN5P")]
    public void VoxelDetect_KnownExteriorElement_IsExterior(string modelFile, string globalId)
    {
        var result = new VoxelFloodFillDetector(0.25).Detect(LoadModel(modelFile).Elements);
        FindClassification(result, globalId).IsExterior.Should().BeTrue();
    }

    /// <summary>
    /// The most important test in this class. Element 2yrIoyurn0lPXgxy2$CNaj in control.ifc bounds the airwell shaft on one face and the enclosed
    /// building interior on the other. Because the shaft is open to the sky, the exterior flood-fill reaches it from above — the element's voxels touch
    /// exterior voxels and MUST be classified exterior.
    /// </summary>
    [Fact]
    public void VoxelDetect_Control_AirwellAdjacentWall_IsExterior()
    {
        var result = new VoxelFloodFillDetector(0.25).Detect(LoadModel("airwell/control.ifc").Elements);
        FindClassification(result, "2yrIoyurn0lPXgxy2$CNaj").IsExterior.Should().BeTrue();
    }

    // ── RayCasting — same geometric claims, different algorithm ─────────────

    /// <summary>
    /// Outer-perimeter walls that RayCasting correctly classifies as exterior — rays fired from their outward-facing triangles escape freely. Three
    /// elements present in the VoxelFloodFill theory are intentionally absent here because their face normals point into a bounded pocket (balcony
    /// alcove or narrow recess), causing all rays to be blocked by the pocket walls regardless of parameter tuning:
    /// <list type="bullet">
    /// <item>with one balcony.ifc — 1wPpdLZdX3efKBKkOInIeg, 1wPpdLZdX3efKBKkOInIef</item> <item>small recess.ifc    — 0mEhezLTj6DBaCuBWyJoXC</item>
    /// </list>
    /// These omissions are a documented RayCasting limitation, not missing test coverage.
    /// </summary>
    [Theory]
    [InlineData("airwell/big recess.ifc",       "0mEhezLTj6DBaCuBWyJo8X")]
    [InlineData("airwell/big recess.ifc",       "0mEhezLTj6DBaCuBWyJo8I")]
    [InlineData("airwell/big recess.ifc",       "0mEhezLTj6DBaCuBWyJo2A")]
    [InlineData("airwell/big recess.ifc",       "0mEhezLTj6DBaCuBWyJo8d")]
    [InlineData("airwell/with one balcony.ifc", "1wPpdLZdX3efKBKkOInIeM")]
    [InlineData("airwell/with one balcony.ifc", "1wPpdLZdX3efKBKkOInIfD")]
    // 1wPpdLZdX3efKBKkOInIeg and 1wPpdLZdX3efKBKkOInIef omitted: balcony-pocket normals → all rays blocked
    [InlineData("airwell/small recess.ifc", "0mEhezLTj6DBaCuBWyJoPN")]
    // 0mEhezLTj6DBaCuBWyJoXC omitted: narrow recess pocket → all rays blocked
    [InlineData("airwell/small recess.ifc", "0mEhezLTj6DBaCuBWyJoeH")]
    [InlineData("airwell/small recess.ifc", "0mEhezLTj6DBaCuBWyJoPD")]
    [InlineData("airwell/round.ifc",        "1wPpdLZdX3efKBKkOInJ4E")]
    [InlineData("airwell/round.ifc",        "1wPpdLZdX3efKBKkOInJBz")]
    [InlineData("airwell/round.ifc",        "1wPpdLZdX3efKBKkOInJ4N")]
    [InlineData("airwell/round.ifc",        "1wPpdLZdX3efKBKkOInJ6m")]
    [InlineData("airwell/control.ifc",      "2yrIoyurn0lPXgxy2$CGm7")]
    [InlineData("airwell/control.ifc",      "2yrIoyurn0lPXgxy2$CN7I")]
    [InlineData("airwell/control.ifc",      "2yrIoyurn0lPXgxy2$CN5P")]
    public void RayCastDetect_KnownExteriorElement_IsExterior(string modelFile, string globalId)
    {
        var result = new RayCastingDetector().Detect(LoadModel(modelFile).Elements);
        FindClassification(result, globalId).IsExterior.Should().BeTrue();
    }

    /// <summary>
    /// Airwell-adjacent wall under RayCasting — documents a known algorithm blind spot. RayCasting fires rays from each triangle in the direction
    /// of its face normal. The shaft-facing triangles have normals pointing INTO the enclosed shaft; rays hit the opposite wall and are blocked.
    /// Contrast with VoxelDetect_Control_AirwellAdjacentWall_IsExterior: this divergence is a key dissertation finding.
    /// </summary>
    [Fact]
    public void RayCastDetect_Control_AirwellAdjacentWall_IsInterior_KnownBlindSpot()
    {
        var result = new RayCastingDetector().Detect(LoadModel("airwell/control.ifc").Elements);

        FindClassification(result, "2yrIoyurn0lPXgxy2$CNaj").IsExterior.Should().BeFalse(
            "RayCasting blind spot: shaft-facing normals fire into enclosed space, " + "connectivity through the open airwell top is not detected");
    }

    // ── Strategy agreement ───────────────────────────────────────────────────

    /// <summary>
    /// The two strategies produce opposite classifications for the airwell-adjacent wall in control.ifc. This disagreement is the central
    /// dissertation finding for this element class.
    /// </summary>
    [Fact]
    public void BothStrategies_Control_AirwellAdjacentWall_Disagree_VoxelCorrectRayCastMisses()
    {
        var elements = LoadModel("airwell/control.ifc").Elements;

        var voxelResult = new VoxelFloodFillDetector(voxelSize: 0.25).Detect(elements);
        var raycastResult = new RayCastingDetector().Detect(elements);

        var voxelClass = FindClassification(voxelResult,     "2yrIoyurn0lPXgxy2$CNaj");
        var raycastClass = FindClassification(raycastResult, "2yrIoyurn0lPXgxy2$CNaj");

        voxelClass.IsExterior.Should().BeTrue("VoxelFloodFill: BFS reaches the shaft from the open top → exterior via connectivity");

        raycastClass.IsExterior.Should().BeFalse("RayCasting: shaft-facing triangles never escape → classified interior (known blind spot)");
    }

    // ────────────────────────────────────────────────────────────────────────

    private static ElementClassification FindClassification(DetectionResult result, string globalId)
    {
        return result.Classifications.First(c => c.Element.GlobalId == globalId);
    }
}
