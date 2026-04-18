using IfcEnvelopeMapper.Core.Loading;

namespace IfcEnvelopeMapper.Tests.Loading;

public class DefaultElementFilterTests
{
    private readonly DefaultElementFilter _default = new();

    [Theory]
    [InlineData("IfcWall")]
    [InlineData("IfcCurtainWall")]
    [InlineData("IfcCurtainWallPanel")]
    [InlineData("IfcStairFlight")]
    [InlineData("IfcSlab")]
    public void Default_IncludesConstructiveTypes(string ifcType)
    {
        _default.Include(ifcType).Should().BeTrue();
    }

    [Theory]
    [InlineData("IfcFooting")]
    [InlineData("IfcBuildingElementProxy")]
    [InlineData("IfcOpeningElement")]
    [InlineData("UnknownType")]
    public void Default_ExcludesNonConstructiveOrUnknownTypes(string ifcType)
    {
        _default.Include(ifcType).Should().BeFalse();
    }

    [Fact]
    public void Default_IsCaseSensitive()
    {
        _default.Include("ifcwall").Should().BeFalse();
        _default.Include("IfcWall").Should().BeTrue();
    }

    [Fact]
    public void CustomSet_OverridesDefault()
    {
        var filter = new DefaultElementFilter(
            new HashSet<string>(StringComparer.Ordinal) { "IfcFooting" });

        filter.Include("IfcFooting").Should().BeTrue();
        filter.Include("IfcWall").Should().BeFalse();
    }

    [Fact]
    public void CustomSet_Empty_ExcludesEverything()
    {
        var filter = new DefaultElementFilter(
            new HashSet<string>(StringComparer.Ordinal));

        filter.Include("IfcWall").Should().BeFalse();
    }
}
