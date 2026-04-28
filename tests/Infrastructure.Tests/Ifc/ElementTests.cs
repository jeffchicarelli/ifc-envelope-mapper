using IfcEnvelopeMapper.Infrastructure.Ifc;
using Xbim.Ifc4.Interfaces;

namespace IfcEnvelopeMapper.Infrastructure.Tests.Ifc;

[Trait("Category", "Integration")]
public sealed class ElementTests : IfcTestBase
{
    public ElementTests() : base("duplex.ifc") { }

    // ───── Equality (by GlobalId) ─────

    [Fact]
    public void Equals_TwoReferencesToSameLoadedElement_AreEqual()
    {
        var first  = Elem(0);
        var second = Model.Elements.OfType<Element>().First(e => e.GlobalId == first.GlobalId);

        first.Equals(second).Should().BeTrue();
        first.GetHashCode().Should().Be(second.GetHashCode());
    }

    [Fact]
    public void Equals_DifferentElements_AreNotEqual()
    {
        var a = Elem(0);
        var b = Elem(1);

        a.GlobalId.Should().NotBe(b.GlobalId);
        a.Equals(b).Should().BeFalse();
    }

    [Fact]
    public void Equals_AgainstNullOrOtherType_ReturnsFalse()
    {
        var      a           = Elem(0);
        Element? nullElement = null;
        object?  nullObject  = null;

        a.Equals(nullElement).Should().BeFalse();
        a.Equals(nullObject).Should().BeFalse();
        a.Equals("not an element").Should().BeFalse();
    }

    [Fact]
    public void HashSet_DeduplicatesByGlobalId()
    {
        // Equality contract: HashSet<Element> works without a custom comparer.
        var firstFive = Model.Elements.Take(5).Cast<Element>().ToList();
        var dupes     = firstFive.Concat(firstFive);
        var set       = new HashSet<Element>(dupes);

        set.Count.Should().Be(5);
    }

    // ───── Lazy mesh / bbox ─────

    [Fact]
    public void GetMesh_CalledTwice_ReturnsSameInstance()
    {
        // Lazy<DMesh3> caches the materialised value — second call must return
        // the exact same reference, not a new clone.
        var element = Elem(0);

        var first  = element.GetMesh();
        var second = element.GetMesh();

        ReferenceEquals(first, second).Should().BeTrue();
    }

    [Fact]
    public void GetBoundingBox_CalledTwice_ReturnsEqualValue()
    {
        var element = Elem(0);

        var first  = element.GetBoundingBox();
        var second = element.GetBoundingBox();

        first.Min.Distance(second.Min).Should().BeLessThan(1e-12);
        first.Max.Distance(second.Max).Should().BeLessThan(1e-12);
    }

    [Fact]
    public void GetBoundingBox_IsNonDegenerateForElementsWithGeometry()
    {
        var element = Model.Elements.Cast<Element>().First(e => e.GetMesh().VertexCount > 8);
        var bbox    = element.GetBoundingBox();

        bbox.Max.x.Should().BeGreaterThan(bbox.Min.x);
        bbox.Max.y.Should().BeGreaterThan(bbox.Min.y);
        bbox.Max.z.Should().BeGreaterThan(bbox.Min.z);
    }

    // ───── IProductEntity ─────

    [Fact]
    public void GetIfcProduct_ReturnsNonNullProductWithMatchingGlobalId()
    {
        var element = Elem(0);

        var product = element.GetIfcProduct();

        product.Should().NotBeNull();
        product.GlobalId.ToString().Should().Be(element.GlobalId);
    }

    [Fact]
    public void IfcType_DerivesFromIIfcProductRuntimeType()
    {
        var ifcType = Model.Elements[0].IfcType;

        // Must start with "Ifc" (xBIM convention).
        ifcType.Should().StartWith("Ifc");
        ifcType.Should().NotBeNullOrWhiteSpace();
    }

    // ───── Children semantics ─────

    [Fact]
    public void Atomic_Element_HasEmptyChildren()
    {
        // duplex has plenty of atomic elements (walls, slabs).
        var atomic = Model.Elements
            .Cast<Element>()
            .First(e => e.GetIfcProduct() is not IIfcCurtainWall
                     && e.GetIfcProduct() is not IIfcRoof
                     && e.Children.Count == 0);

        atomic.Children.Should().BeEmpty();
    }

    private Element Elem(int index) => (Element)Model.Elements[index];
}
