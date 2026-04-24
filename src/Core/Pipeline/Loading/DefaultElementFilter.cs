namespace IfcEnvelopeMapper.Core.Pipeline.Loading;

/// <summary>
/// IFC-type allow-list controlling which entities the loader emits. The default set
/// covers the constructive element types relevant to envelope detection; callers can
/// pass a custom set (e.g., for specialized IFC profiles or reduced-scope runs).
/// </summary>
public sealed class DefaultElementFilter
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
