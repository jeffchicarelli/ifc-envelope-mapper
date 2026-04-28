using g4;
using IfcEnvelopeMapper.Domain.Interfaces;
using Xbim.Ifc4.Interfaces;

namespace IfcEnvelopeMapper.Infrastructure.Ifc;

/// <summary>
/// Domain entity wrapping an <see cref="IIfcProduct"/>. Carries lazy mesh and
/// bounding box, a spatial context bundle, and an optional list of children for
/// composite IFC products (e.g. IfcCurtainWall, IfcRoof). Equality is identity
/// by <see cref="GlobalId"/>.
/// </summary>
public class Element :
    IElement,
    IProductEntity,
    IEquatable<Element>
{
    protected readonly IfcProductContext _ctx;
    protected readonly Lazy<DMesh3> _lazyMesh;
    protected readonly Lazy<AxisAlignedBox3d> _lazyBbox;

    /// <summary>Constituent elements of a composite product (e.g. panels of an IfcCurtainWall). Empty for standalone elements.</summary>
    public IReadOnlyList<Element> Children { get; init; } = [];
    /// <summary>GlobalId of the composite product that owns this element, or <c>null</c> for standalones.</summary>
    public string? GroupGlobalId { get; init; }

    /// <inheritdoc/>
    public string GlobalId => _ctx.Product.GlobalId;
    /// <inheritdoc/>
    public string? Name    => _ctx.Product.Name;
    /// <inheritdoc/>
    public string IfcType  => _ctx.Product.GetType().Name;

    /// <inheritdoc/>
    public IIfcProduct GetIfcProduct()           => _ctx.Product;
    /// <inheritdoc/>
    public IIfcSite? GetIfcSite()                => _ctx.Site;
    /// <inheritdoc/>
    public IIfcBuilding? GetIfcBuilding()        => _ctx.Building;
    /// <inheritdoc/>
    public IIfcBuildingStorey? GetIfcStorey()    => _ctx.Storey;

    /// <inheritdoc/>
    public DMesh3 GetMesh()                      => _lazyMesh.Value;
    /// <inheritdoc/>
    public AxisAlignedBox3d GetBoundingBox()     => _lazyBbox.Value;

    public bool Equals(Element? other) =>
        other is not null && GlobalId == other.GlobalId;

    public override bool Equals(object? obj) => Equals(obj as Element);

    public override int GetHashCode() => GlobalId.GetHashCode();

    public Element(
        IfcProductContext ctx,
        Lazy<DMesh3> lazyMesh,
        Lazy<AxisAlignedBox3d> lazyBbox)
    {
        _ctx = ctx;
        _lazyMesh = lazyMesh;
        _lazyBbox = lazyBbox;
    }

    /// <summary>
    /// Sharing constructor: a derived class reuses the lazy state of an
    /// existing instance, so the same cached mesh/bbox is shared.
    /// </summary>
    protected Element(Element other)
    {
        _ctx = other._ctx;
        _lazyMesh = other._lazyMesh;
        _lazyBbox = other._lazyBbox;
        Children = other.Children;
        GroupGlobalId = other.GroupGlobalId;
    }
}
