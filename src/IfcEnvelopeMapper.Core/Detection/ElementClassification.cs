using IfcEnvelopeMapper.Core.Element;
using IfcEnvelopeMapper.Core.Surface;

namespace IfcEnvelopeMapper.Core.Detection;

public sealed class ElementClassification
{
    public BuildingElement Element { get; }
    public bool IsExterior { get; }
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
