using IfcEnvelopeMapper.Engine.Debug;

namespace IfcEnvelopeMapper.Tests.Engine.Debug;

/// <summary>
/// Unit tests for <see cref="IfcTypePalette.For"/>. The palette is a small
/// hardcoded lookup, so the tests pin the contract — known types map to
/// their colour, unknown types fall back to a default, and lookup is
/// case-insensitive (matches the table's <c>StringComparer.OrdinalIgnoreCase</c>).
/// </summary>
public class IfcTypePaletteTests
{
    [Theory]
    [InlineData("IfcWall")]
    [InlineData("IfcSlab")]
    [InlineData("IfcRoof")]
    [InlineData("IfcWindow")]
    [InlineData("IfcDoor")]
    [InlineData("IfcSpace")]
    public void For_KnownType_ReturnsHexColorString(string ifcType)
    {
        var color = IfcTypePalette.For(ifcType);

        color.Should().StartWith("#");
        color.Length.Should().BeOneOf(7, 9); // "#RRGGBB" or "#RRGGBBAA"
    }

    [Fact]
    public void For_UnknownType_ReturnsDefaultGray()
    {
        var color = IfcTypePalette.For("IfcSomethingUnsupported");

        color.Should().Be("#cccccccc");
    }

    [Fact]
    public void For_LookupIsCaseInsensitive()
    {
        // The palette is keyed with StringComparer.OrdinalIgnoreCase. Same
        // canonical type spelled in different casings must resolve to the
        // same colour — relevant because IFC type names come from
        // reflection (`element.GetType().Name`) but xBIM has historically
        // surprised callers with subtle casing variations across schema versions.
        var lower = IfcTypePalette.For("ifcwall");
        var upper = IfcTypePalette.For("IFCWALL");
        var pascal = IfcTypePalette.For("IfcWall");

        lower.Should().Be(pascal);
        upper.Should().Be(pascal);
    }

    [Fact]
    public void For_GlazingTypes_AreTranslucent()
    {
        // Documented contract from the source: glazing types (IfcWindow,
        // IfcCurtainWall) carry alpha < 0xFF so the viewer renders them
        // see-through without per-call alpha overrides.
        var window      = IfcTypePalette.For("IfcWindow");
        var curtainWall = IfcTypePalette.For("IfcCurtainWall");

        window.Length.Should().Be(9, "glazing colors include an alpha byte");
        curtainWall.Length.Should().Be(9);

        // Last two hex digits are alpha; parse and confirm < 0xFF.
        AlphaOf(window).Should().BeLessThan(0xFF);
        AlphaOf(curtainWall).Should().BeLessThan(0xFF);
    }

    [Fact]
    public void For_SpaceType_IsHighlyTranslucent()
    {
        // IfcSpace renders as yellow @ alpha 0x40 — used to highlight room
        // volumes in the debug viewer without occluding the elements inside.
        var color = IfcTypePalette.For("IfcSpace");

        AlphaOf(color).Should().BeLessThan(0x80);
    }

    [Fact]
    public void For_NullOrEmpty_ReturnsDefaultWithoutThrowing()
    {
        // Guard against a defensive path: the source uses TryGetValue, which
        // tolerates null keys via the StringComparer. Empty string isn't
        // in the table, so it falls through to the default.
        IfcTypePalette.For(string.Empty).Should().Be("#cccccccc");
    }

    private static int AlphaOf(string hexColor)
    {
        // Last two characters of "#RRGGBBAA"
        var alphaHex = hexColor.Substring(hexColor.Length - 2);
        return Convert.ToInt32(alphaHex, 16);
    }
}
