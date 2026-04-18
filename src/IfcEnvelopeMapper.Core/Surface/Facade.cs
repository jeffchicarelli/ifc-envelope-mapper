using g4;
using IfcEnvelopeMapper.Core.Element;

namespace IfcEnvelopeMapper.Core.Surface;

public sealed class Facade
{
    public string Id { get; }
    public Envelope Envelope { get; }
    public IReadOnlyList<Face> Faces { get; }
    public DMesh3 FacadeShell { get; }
    public Vector3d DominantNormal { get; }
    public double AzimuthDegrees { get; }
    public IReadOnlyList<BuildingElement> Elements { get; }

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
