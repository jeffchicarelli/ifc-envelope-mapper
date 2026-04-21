namespace IfcEnvelopeMapper.Geometry.Debug;

// Semantic color palette for debug visualization, keyed by IFC entity type.
// Loosely follows the Solibri / BIMcollab convention: warm tones for envelope
// elements (walls, roof, doors), gray for structure (slabs, columns, beams),
// cyan-translucent for glazing, yellow-translucent for spaces.
//
// Alpha channel is included so the viewer can render glazing and spaces
// see-through without the caller having to append alpha per call site.
public static class IfcTypePalette
{
    private static readonly Dictionary<string, string> Colors = new(StringComparer.OrdinalIgnoreCase)
    {
        { "IfcWall",             "#d4c4a8c0" },
        { "IfcWallStandardCase", "#d4c4a8c0" },
        { "IfcSlab",             "#b0b0b0c0" },
        { "IfcRoof",             "#a0522dc0" },
        { "IfcWindow",           "#87ceeb80" },
        { "IfcDoor",             "#8b5a2bc0" },
        { "IfcColumn",           "#909090c0" },
        { "IfcBeam",             "#808080c0" },
        { "IfcStair",            "#b8860bc0" },
        { "IfcStairFlight",      "#b8860bc0" },
        { "IfcRailing",          "#696969c0" },
        { "IfcCurtainWall",      "#87ceeb80" },
        { "IfcMember",           "#707070c0" },
        { "IfcPlate",            "#909090c0" },
        { "IfcSpace",            "#ffff0040" },
        { "IfcCovering",         "#c0b090c0" },
    };

    public static string For(string ifcType) =>
        Colors.TryGetValue(ifcType, out var c) ? c : "#cccccccc";
}
