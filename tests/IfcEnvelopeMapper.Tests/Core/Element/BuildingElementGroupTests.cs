using g4;
using IfcEnvelopeMapper.Core.Element;

namespace IfcEnvelopeMapper.Tests.Core.Element;

public class BuildingElementGroupTests
{
    private static BuildingElementGroup Make(string globalId, DMesh3? ownMesh = null) => new()
    {
        GlobalId = globalId,
        IfcType = "IfcStair",
        OwnMesh = ownMesh,
        Elements = [],
    };

    [Fact]
    public void Groups_WithSameGlobalId_AreEqual()
    {
        var a = Make("stair-1");
        var b = Make("stair-1");

        a.Should().Be(b);
    }

    [Fact]
    public void Groups_WithDifferentGlobalId_AreNotEqual()
    {
        var a = Make("stair-1");
        var b = Make("stair-2");

        a.Should().NotBe(b);
    }

    [Fact]
    public void GetHashCode_IsConsistentWithEquals()
    {
        var a = Make("stair-1");
        var b = Make("stair-1");

        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void OwnMesh_CanBeNull()
    {
        var group = Make("stair-1", ownMesh: null);

        group.OwnMesh.Should().BeNull();
    }

    [Fact]
    public void Equals_WithNull_ReturnsFalse()
    {
        var a = Make("stair-1");

        a.Equals(null).Should().BeFalse();
    }
}
