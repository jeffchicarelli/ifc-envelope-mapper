using IfcEnvelopeMapper.Core.Building;

namespace IfcEnvelopeMapper.Tests;

public class BuildingElementContextTests
{
    [Fact]
    public void Default_AllPropertiesAreNull()
    {
        var ctx = default(BuildingElementContext);

        ctx.SiteId.Should().BeNull();
        ctx.BuildingId.Should().BeNull();
        ctx.StoreyId.Should().BeNull();
    }

    [Fact]
    public void Contexts_WithSameValues_AreEqual()
    {
        var a = new BuildingElementContext("site-1", "bldg-1", "storey-1");
        var b = new BuildingElementContext("site-1", "bldg-1", "storey-1");

        a.Should().Be(b);
    }
}
