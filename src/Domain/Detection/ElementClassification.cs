using IfcEnvelopeMapper.Domain.Interfaces;
using IfcEnvelopeMapper.Domain.Surface;

namespace IfcEnvelopeMapper.Domain.Detection;

/// <summary>
/// A single <see cref="IElement"/>'s verdict from a detection strategy:
/// whether it belongs to the exterior envelope, plus the specific faces that do.
/// An interior element carries an empty <see cref="ExternalFaces"/> list.
/// </summary>
public sealed class ElementClassification
{
    /// <summary>The classified element.</summary>
    public IElement Element { get; }
    /// <summary><c>true</c> when the element belongs to the exterior envelope.</summary>
    public bool IsExterior { get; }

    /// <summary>Faces of <see cref="Element"/> that were judged to face outward. Empty for interior elements.</summary>
    public IReadOnlyList<Face> ExternalFaces { get; }

    public ElementClassification(
        IElement element,
        bool isExterior,
        IReadOnlyList<Face> externalFaces)
    {
        Element = element;
        IsExterior = isExterior;
        ExternalFaces = externalFaces;
    }
}
