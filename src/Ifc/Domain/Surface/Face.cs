using g4;
using IfcEnvelopeMapper.Ifc.Domain;

namespace IfcEnvelopeMapper.Ifc.Domain.Surface;

/// <summary>
/// A near-planar region of an element's mesh — one "side" of the element, in practice.
/// A <see cref="Face"/> bundles the source triangles with their best-fit plane so later
/// stages can reason about orientation without re-scanning the geometry.
///
///     mesh triangles                    Face
///      ┌───────┐                         ┌──────────────────┐
///      │△△△△△△│  ── PcaFaceExtractor ──▶│ TriangleIds[…]   │
///      │△△△△△△│   groups coplanar tris  │ FittedPlane   ─▶ n̂│
///      └───────┘                         │ Area, Centroid   │
///                                        └──────────────────┘
///
/// </summary>
public sealed class Face
{
    public Element Element { get; }

    /// <summary>Ids of the source element's triangles that make up this face.</summary>
    public IReadOnlyList<int> TriangleIds { get; }

    /// <summary>Best-fit plane through those triangles (PCA).</summary>
    public Plane3d FittedPlane { get; }

    /// <summary>Unit outward normal of <see cref="FittedPlane"/>.</summary>
    public Vector3d Normal => FittedPlane.Normal;
    public double Area { get; }
    public Vector3d Centroid { get; }

    public Face(
        Element element,
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
