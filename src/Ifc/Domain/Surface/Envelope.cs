using g4;

namespace IfcEnvelopeMapper.Ifc.Domain.Surface;

/// <summary>
/// The full exterior skin of the building — every outward-facing <see cref="Face"/>,
/// grouped into a single closed mesh. Downstream stages slice the envelope into
/// per-orientation <see cref="Facade"/>s.
///
///      ╱────────────╲
///     ╱             ╱│        Shell    = DMesh3 wrapping the whole exterior
///    ╱  envelope   ╱ │        Faces    = fitted-plane pieces of that shell
///   ╱─────────────╱  │        Elements = unique Elements contributing faces
///   │             │  ╱
///   │             │ ╱
///   └─────────────┘╱
///
/// </summary>
public sealed class Envelope
{
    public DMesh3 Shell { get; }
    public IReadOnlyList<Face> Faces { get; }
    public IReadOnlyList<Element> Elements { get; }

    public Envelope(DMesh3 shell, IReadOnlyList<Face> faces)
    {
        Shell = shell;
        Faces = faces;
        Elements = faces.Select(f => f.Element).Distinct().ToList();
    }
}
