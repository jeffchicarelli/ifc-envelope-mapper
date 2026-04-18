using g4;
using IfcEnvelopeMapper.Core.Element;

namespace IfcEnvelopeMapper.Tests.Element;

public class BuildingElementTests
{
    private static BuildingElement Make(string globalId) => new()
    {
        GlobalId = globalId,
        IfcType = "IfcWall",
        Mesh = new DMesh3(),
    };

    [Fact]
    public void Elements_WithSameGlobalId_AreEqual()
    {
        var a = Make("abc123");
        var b = Make("abc123");

        a.Should().Be(b);
    }

    [Fact]
    public void Elements_WithDifferentGlobalId_AreNotEqual()
    {
        var a = Make("abc123");
        var b = Make("xyz789");

        a.Should().NotBe(b);
    }

    [Fact]
    public void GetHashCode_IsConsistentWithEquals()
    {
        var a = Make("abc123");
        var b = Make("abc123");

        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Equals_WithNull_ReturnsFalse()
    {
        var a = Make("abc123");

        a.Equals(null).Should().BeFalse();
    }
}
