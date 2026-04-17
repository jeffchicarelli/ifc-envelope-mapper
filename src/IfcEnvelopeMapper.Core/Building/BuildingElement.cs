namespace IfcEnvelopeMapper.Core.Building;

public sealed class BuildingElement
{
    public string GlobalId { get; }
    public string IfcType { get; }

    public BuildingElement(string globalId, string ifcType)
    {
        GlobalId = globalId;
        IfcType = ifcType;
    }
}
