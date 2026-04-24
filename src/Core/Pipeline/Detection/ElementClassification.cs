using IfcEnvelopeMapper.Core.Domain.Element;
using IfcEnvelopeMapper.Core.Domain.Surface;

namespace IfcEnvelopeMapper.Core.Pipeline.Detection;

/// <summary>
/// A single <see cref="BuildingElement"/>'s verdict from a detection strategy:
/// whether it belongs to the exterior envelope, plus the specific faces that do.
/// An interior element carries an empty <see cref="ExternalFaces"/> list.
/// </summary>
public sealed class ElementClassification
{
    public BuildingElement Element { get; }
    public bool IsExterior { get; }

    /// <summary>Faces of <see cref="Element"/> that were judged to face outward. Empty for interior elements.</summary>
    public IReadOnlyList<Face> ExternalFaces { get; }

    public ElementClassification(
        BuildingElement element,
        bool isExterior,
        IReadOnlyList<Face> externalFaces)
    {
        Element = element;
        IsExterior = isExterior;
        ExternalFaces = externalFaces;
    }
}
