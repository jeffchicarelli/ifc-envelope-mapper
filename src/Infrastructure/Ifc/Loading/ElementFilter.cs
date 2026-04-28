namespace IfcEnvelopeMapper.Infrastructure.Ifc.Loading;

/// <summary>
/// IFC-type allow-list controlling which entities the loader emits. The default set
/// covers the constructive element types relevant to envelope detection; callers can
/// pass a custom set (e.g., for specialized IFC profiles or reduced-scope runs).
/// </summary>
public sealed class ElementFilter
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

            // Aggregators (become ElementGroup)
            "IfcCurtainWall", "IfcStair", "IfcRamp", "IfcRailing",

            // Aggregator children (become Element inside a Group)
            "IfcCurtainWallPanel",
            "IfcStairFlight",
            "IfcRampFlight",
        };

    private readonly IReadOnlySet<string> _types;

    /// <summary>Creates a filter using <paramref name="types"/> when provided, otherwise the built-in allow-list.</summary>
    public ElementFilter(IReadOnlySet<string>? types = null)
        => _types = types ?? DefaultTypes;

    /// <summary><c>true</c> when <paramref name="ifcType"/> is in the allow-list.</summary>
    public bool Include(string ifcType) => _types.Contains(ifcType);
}
