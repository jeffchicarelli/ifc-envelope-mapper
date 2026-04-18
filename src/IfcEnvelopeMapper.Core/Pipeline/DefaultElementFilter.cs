namespace IfcEnvelopeMapper.Core.Pipeline;

public sealed class DefaultElementFilter : IElementFilter
{
    private static readonly IReadOnlySet<string> DefaultTypes =
        new HashSet<string>(StringComparer.Ordinal)
        {
            // Standalone constructive elements (van der Vaart 2022)
            "IfcWall", "IfcWallStandardCase",
            "IfcSlab", "IfcRoof",
            "IfcWindow", "IfcDoor",
            "IfcColumn", "IfcBeam",
            "IfcCovering", "IfcMember", "IfcPlate",

            // Aggregators (become BuildingElementGroup)
            "IfcCurtainWall", "IfcStair", "IfcRamp", "IfcRailing",

            // Aggregator children (become BuildingElement inside a Group)
            "IfcCurtainWallPanel",
            "IfcStairFlight",
            "IfcRampFlight",
        };

    private readonly IReadOnlySet<string> _types;

    public DefaultElementFilter(IReadOnlySet<string>? types = null)
        => _types = types ?? DefaultTypes;

    public bool Include(string ifcType) => _types.Contains(ifcType);
}
