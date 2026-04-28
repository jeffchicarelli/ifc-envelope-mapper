using g4;
using IfcEnvelopeMapper.Domain.Interfaces;

namespace IfcEnvelopeMapper.Domain.Surface;

/// <summary>
/// A near-planar region of an element's mesh. Bundles the source triangle ids
/// with a best-fit plane so orientation queries require no further mesh traversal.
///
///     mesh triangles                      Face
///      ┌───────┐                         ┌──────────────────┐
///      │△△△△△△│  ── IFaceExtractor ────▶│ TriangleIds[…]   │
///      │△△△△△△│   groups coplanar tris  │ FittedPlane  ─▶ n̂│
///      └───────┘                         │ Area, Centroid   │
///                                        └──────────────────┘
///
/// </summary>
public sealed class Face
{
    /// <summary>The source element this face belongs to.</summary>
    public IElement Element { get; }

    /// <summary>Ids of the source element's triangles that make up this face.</summary>
    public IReadOnlyList<int> TriangleIds { get; }

    /// <summary>Best-fit plane through those triangles (PCA).</summary>
    public Plane3d FittedPlane { get; }

    /// <summary>Unit outward normal of <see cref="FittedPlane"/>.</summary>
    public Vector3d Normal => FittedPlane.Normal;
    /// <summary>Surface area in square world units.</summary>
    public double Area { get; }
    /// <summary>Area-weighted centroid in world coordinates.</summary>
    public Vector3d Centroid { get; }

    public Face(
        IElement element,
        IReadOnlyList<int> triangleIds,
        Plane3d fittedPlane,
        double area,
        Vector3d centroid)
    {
        Element = element;
        TriangleIds = triangleIds;
        FittedPlane = fittedPlane;
        Area = area;
        Centroid = centroid;
    }
}
