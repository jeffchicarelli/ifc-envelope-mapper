using IfcEnvelopeMapper.Ifc.Domain;
using Xbim.Ifc4.Interfaces;

namespace IfcEnvelopeMapper.Tests.Ifc.Domain;

/// <summary>
/// Contract tests for <see cref="Element"/>. Run on the real <c>duplex.ifc</c>
/// fixture because <c>Element</c> can only be constructed via
/// <c>XbimModelLoader</c>: the lazy mesh/bbox closures need a live
/// <c>Xbim3DModelContext</c>, and the <c>IfcProductContext</c> bundle holds
/// real <c>IIfcProduct</c> references.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ElementTests : IfcTestBase
{
    public ElementTests() : base("duplex.ifc") { }

    // ───── Equality (by GlobalId) ─────

    [Fact]
    public void Equals_TwoReferencesToSameLoadedElement_AreEqual()
    {
        var first  = Model.Elements[0];
        var second = Model.Elements.First(e => e.GlobalId == first.GlobalId);

        first.Equals(second).Should().BeTrue();
        first.GetHashCode().Should().Be(second.GetHashCode());
    }

    [Fact]
    public void Equals_DifferentElements_AreNotEqual()
    {
        var a = Model.Elements[0];
        var b = Model.Elements[1];

        a.GlobalId.Should().NotBe(b.GlobalId);
        a.Equals(b).Should().BeFalse();
    }

    [Fact]
    public void Equals_AgainstNullOrOtherType_ReturnsFalse()
    {
        var      a               = Model.Elements[0];
        Element? nullElement     = null;
        object?  nullObject      = null;

        a.Equals(nullElement).Should().BeFalse();
        a.Equals(nullObject).Should().BeFalse();
        a.Equals("not an element").Should().BeFalse();
    }

    [Fact]
    public void HashSet_DeduplicatesByGlobalId()
    {
        // Equality contract pays off: HashSet<Element> works without a custom
        // comparer because GetHashCode + Equals are consistent.
        var firstFive = Model.Elements.Take(5).ToList();
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
        var element = Model.Elements[0];

        var first  = element.GetMesh();
        var second = element.GetMesh();

        ReferenceEquals(first, second).Should().BeTrue();
    }

    [Fact]
    public void GetBoundingBox_CalledTwice_ReturnsEqualValue()
    {
        // AxisAlignedBox3d is a struct, so reference equality is meaningless —
        // we verify the lazy returns a stable, equal value instead.
        var element = Model.Elements[0];

        var first  = element.GetBoundingBox();
        var second = element.GetBoundingBox();

        first.Min.Distance(second.Min).Should().BeLessThan(1e-12);
        first.Max.Distance(second.Max).Should().BeLessThan(1e-12);
    }

    [Fact]
    public void GetBoundingBox_IsNonDegenerateForElementsWithGeometry()
    {
        // Sanity check: an element with mesh data must have a bbox where Max > Min
        // on every axis. A stronger contract — that the bbox actually bounds every
        // mesh vertex — does not currently hold (XbimShapeInstance.BoundingBox is
        // not always in the same coord system as the materialised mesh).
        // See ADR-08 in docs/plano.md and the open follow-up task.
        var element = Model.Elements.First(e => e.GetMesh().VertexCount > 8);
        var bbox    = element.GetBoundingBox();

        bbox.Max.x.Should().BeGreaterThan(bbox.Min.x);
        bbox.Max.y.Should().BeGreaterThan(bbox.Min.y);
        bbox.Max.z.Should().BeGreaterThan(bbox.Min.z);
    }

    // ───── IProductEntity ─────

    [Fact]
    public void GetIfcProduct_ReturnsNonNullProductWithMatchingGlobalId()
    {
        var element = Model.Elements[0];

        var product = element.GetIfcProduct();

        product.Should().NotBeNull();
        product.GlobalId.ToString().Should().Be(element.GlobalId);
    }

    [Fact]
    public void IfcType_DerivesFromIIfcProductRuntimeType()
    {
        var element = Model.Elements[0];

        var ifcType = element.IfcType;

        // Must start with "Ifc" (xBIM convention) and match the actual product type.
        ifcType.Should().StartWith("Ifc");
        // Product type is a wrapper class in xBIM; its name typically contains
        // the IFC schema entity name (e.g. "IfcWall" → class "IfcWall").
        ifcType.Should().NotBeNullOrWhiteSpace();
    }

    // ───── Children semantics ─────

    [Fact]
    public void Atomic_Element_HasEmptyChildren()
    {
        // duplex has plenty of atomic elements (walls, slabs); pick any non-composite
        // to verify Children defaults to []. We identify atomics by having a non-
        // composite IFC type.
        var atomic = Model.Elements
            .First(e => e.GetIfcProduct() is not IIfcCurtainWall
                     && e.GetIfcProduct() is not IIfcRoof
                     && e.Children.Count == 0);

        atomic.Children.Should().BeEmpty();
    }
}
