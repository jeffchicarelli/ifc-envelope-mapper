using IfcEnvelopeMapper.Infrastructure.Visualization;
using IfcEnvelopeMapper.Infrastructure.Visualization.Api;

namespace IfcEnvelopeMapper.Infrastructure.Tests.Visualization;

public class IfcTypePaletteTests
{
    private static readonly Color _default = Color.FromHex("#cccccccc");

    [Theory]
    [InlineData("IfcWall")]
    [InlineData("IfcSlab")]
    [InlineData("IfcRoof")]
    [InlineData("IfcWindow")]
    [InlineData("IfcDoor")]
    [InlineData("IfcSpace")]
    public void For_KnownType_ReturnsTableColor(string ifcType)
    {
        var color = IfcTypePalette.For(ifcType);

        // Known types resolve to entries in the table — anything other than the default fallback.
        color.Should().NotBe(_default);
    }

    [Fact]
    public void For_UnknownType_ReturnsDefaultGray()
    {
        var color = IfcTypePalette.For("IfcSomethingUnsupported");

        color.Should().Be(_default);
    }

    [Fact]
    public void For_LookupIsCaseInsensitive()
    {
        var lower = IfcTypePalette.For("ifcwall");
        var upper = IfcTypePalette.For("IFCWALL");
        var pascal = IfcTypePalette.For("IfcWall");

        lower.Should().Be(pascal);
        upper.Should().Be(pascal);
    }

    [Fact]
    public void For_GlazingTypes_AreTranslucent()
    {
        // Documented contract: glazing types carry alpha < 0xFF.
        var window = IfcTypePalette.For("IfcWindow");
        var curtainWall = IfcTypePalette.For("IfcCurtainWall");

        window.A.Should().BeLessThan(0xFF);
        curtainWall.A.Should().BeLessThan(0xFF);
    }

    [Fact]
    public void For_SpaceType_IsHighlyTranslucent()
    {
        // IfcSpace renders as yellow @ alpha 0x40.
        var color = IfcTypePalette.For("IfcSpace");

        color.A.Should().BeLessThan(0x80);
    }

    [Fact]
    public void For_NullOrEmpty_ReturnsDefaultWithoutThrowing()
    {
        IfcTypePalette.For(string.Empty).Should().Be(_default);
    }
}
