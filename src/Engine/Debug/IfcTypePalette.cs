using IfcEnvelopeMapper.Engine.Debug.Api;

namespace IfcEnvelopeMapper.Engine.Debug;

// Semantic color palette for debug visualization, keyed by IFC entity type.
// Loosely follows the Solibri / BIMcollab convention: warm tones for envelope
// elements (walls, roof, doors), gray for structure (slabs, columns, beams),
// cyan-translucent for glazing, yellow-translucent for spaces.
//
// Alpha channel is included so the viewer can render glazing and spaces
// see-through without the caller having to append alpha per call site.
public static class IfcTypePalette
{
    private static readonly Dictionary<string, Color> Colors = new(StringComparer.OrdinalIgnoreCase)
    {
        { "IfcWall",             Color.FromHex("#d4c4a8c0") },
        { "IfcWallStandardCase", Color.FromHex("#d4c4a8c0") },
        { "IfcSlab",             Color.FromHex("#b0b0b0c0") },
        { "IfcRoof",             Color.FromHex("#a0522dc0") },
        { "IfcWindow",           Color.FromHex("#87ceeb80") },
        { "IfcDoor",             Color.FromHex("#8b5a2bc0") },
        { "IfcColumn",           Color.FromHex("#909090c0") },
        { "IfcBeam",             Color.FromHex("#808080c0") },
        { "IfcStair",            Color.FromHex("#b8860bc0") },
        { "IfcStairFlight",      Color.FromHex("#b8860bc0") },
        { "IfcRailing",          Color.FromHex("#696969c0") },
        { "IfcCurtainWall",      Color.FromHex("#87ceeb80") },
        { "IfcMember",           Color.FromHex("#707070c0") },
        { "IfcPlate",            Color.FromHex("#909090c0") },
        { "IfcSpace",            Color.FromHex("#ffff0040") },
        { "IfcCovering",         Color.FromHex("#c0b090c0") },
    };

    private static readonly Color Default = Color.FromHex("#cccccccc");

    public static Color For(string ifcType) =>
        Colors.TryGetValue(ifcType, out var c) ? c : Default;
}
