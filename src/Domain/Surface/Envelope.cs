using g4;
using IfcEnvelopeMapper.Domain.Interfaces;

namespace IfcEnvelopeMapper.Domain.Surface;

/// <summary>
/// Aggregate of every outward-facing <see cref="Face"/>, combined into a single closed mesh (<see cref="Shell"/>).
/// Sliced into per-orientation <see cref="Facade"/>s by <see cref="Domain.Services.IFacadeGrouper"/>.
/// <code>
///      ╱────────────╲
///     ╱             ╱│        Shell    = DMesh3 wrapping the whole exterior
///    ╱  envelope   ╱ │        Faces    = fitted-plane pieces of that shell
///   ╱─────────────╱  │        Elements = unique IElements contributing faces
///   │             │  ╱
///   │             │ ╱
///   └─────────────┘╱
/// </code>
/// </summary>
public sealed class Envelope
{
    /// <summary>Merged mesh of all exterior faces.</summary>
    public DMesh3 Shell { get; }

    /// <summary>Individual faces that make up the exterior skin.</summary>
    public IReadOnlyList<Face> Faces { get; }

    /// <summary>Distinct elements that contribute at least one face to the envelope.</summary>
    public IReadOnlyList<IElement> Elements { get; }

    /// <summary>Constructs an envelope from the merged <paramref name="shell"/> mesh and its constituent <paramref name="faces"/>.</summary>
    public Envelope(DMesh3 shell, IReadOnlyList<Face> faces)
    {
        Shell = shell;
        Faces = faces;
        Elements = faces.Select(f => f.Element).Distinct().ToList();
    }
}
