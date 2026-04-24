using g4;
using IfcEnvelopeMapper.Core.Domain.Element;

namespace IfcEnvelopeMapper.Core.Domain.Surface;

public sealed class Envelope
{
    public DMesh3 Shell { get; }
    public IReadOnlyList<Face> Faces { get; }
    public IReadOnlyList<BuildingElement> Elements { get; }

    public Envelope(DMesh3 shell, IReadOnlyList<Face> faces)
    {
        Shell = shell;
        Faces = faces;
        Elements = faces.Select(f => f.Element).Distinct().ToList();
    }
}
