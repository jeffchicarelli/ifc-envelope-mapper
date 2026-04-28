using IfcEnvelopeMapper.Infrastructure.Visualization.Api;

namespace IfcEnvelopeMapper.Infrastructure.Visualization;

/// <summary>
/// Semantic color palette for debug visualization, keyed by IFC entity type. Loosely follows the Solibri /
/// BIMcollab convention: warm tones for envelope elements (walls, roof, doors), gray for structure (slabs,
/// columns, beams), cyan-translucent for glazing, yellow-translucent for spaces. The alpha channel lets the
/// viewer render glazing and spaces see-through without per-call-site alpha appending.
/// </summary>
public static class IfcTypePalette
{
    private static readonly Dictionary<string, Color> _colors = new(StringComparer.OrdinalIgnoreCase)
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

    private static readonly Color _default = Color.FromHex("#cccccccc");

    public static Color For(string ifcType)
    {
        return _colors.TryGetValue(ifcType, out var c) ? c : _default;
    }
}
