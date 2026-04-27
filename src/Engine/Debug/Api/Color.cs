using System.Globalization;
using System.Numerics;

namespace IfcEnvelopeMapper.Engine.Debug.Api;

/// <summary>
/// RGBA colour for debug emissions. Stored as four bytes; equality is by value.
/// Failures (bad hex input) surface at <see cref="FromHex"/> rather than deep
/// inside the GLB encoder, so the offending call site is the one that throws.
/// </summary>
public readonly record struct Color(byte R, byte G, byte B, byte A = 255)
{
    /// <summary>
    /// Parses <c>#RRGGBB</c> or <c>#RRGGBBAA</c>. Leading <c>#</c> is optional.
    /// Throws <see cref="FormatException"/> on any other shape.
    /// </summary>
    public static Color FromHex(string hex)
    {
        var span = hex.AsSpan().TrimStart('#');
        if (span.Length is not 6 and not 8)
        {
            throw new FormatException($"Expected '#RRGGBB' or '#RRGGBBAA', got '{hex}'.");
        }

        var r = byte.Parse(span[0..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var g = byte.Parse(span[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var b = byte.Parse(span[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var a = span.Length == 8
            ? byte.Parse(span[6..8], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : (byte)255;
        return new Color(r, g, b, a);
    }

    public static readonly Color Red     = new(255, 0,   0);
    public static readonly Color Green   = new(0,   255, 0);
    public static readonly Color Blue    = new(0,   0,   255);
    public static readonly Color Yellow  = new(255, 255, 0);
    public static readonly Color Cyan    = new(0,   255, 255);
    public static readonly Color Magenta = new(255, 0,   255);
    public static readonly Color White   = new(255, 255, 255);
    public static readonly Color Gray    = new(204, 204, 204);

    internal Vector4 ToVector4() => new(R / 255f, G / 255f, B / 255f, A / 255f);
}
