using g4;
using IfcEnvelopeMapper.Domain.Interfaces;

namespace IfcEnvelopeMapper.Domain.Surface;

/// <summary>
/// One oriented slice of an <see cref="Envelope"/> — typically a cardinal wall plus
/// anything leaning within a small angular tolerance of it (N/E/S/W, plus roof/floor).
///
///       north facade                       azimuth
///       (dominant n̂ ≈ +Y)                (compass bearing
///       ┌───────────────┐                 of n̂ projected to
///       │   faces: 4    │                 the XY plane, in
///       │   elements: 7 │                 degrees from north)
///       └───────────────┘
///
/// </summary>
public sealed class Facade
{
    public string Id { get; }
    public Envelope Envelope { get; }
    public IReadOnlyList<Face> Faces { get; }

    /// <summary>Mesh of just this facade's faces — subset of <see cref="Envelope.Shell"/>.</summary>
    public DMesh3 FacadeShell { get; }

    /// <summary>Area-weighted average normal of the faces — drives the facade's orientation.</summary>
    public Vector3d DominantNormal { get; }

    /// <summary>Compass bearing of <see cref="DominantNormal"/> projected onto XY, in degrees from +Y (north).</summary>
    public double AzimuthDegrees { get; }
    public IReadOnlyList<IElement> Elements { get; }

    public Facade(
        string id,
        Envelope envelope,
        IReadOnlyList<Face> faces,
        DMesh3 facadeShell,
        Vector3d dominantNormal,
        double azimuthDegrees)
    {
        Id = id;
        Envelope = envelope;
        Faces = faces;
        FacadeShell = facadeShell;
        DominantNormal = dominantNormal;
        AzimuthDegrees = azimuthDegrees;
        Elements = faces.Select(f => f.Element).Distinct().ToList();
    }
}
