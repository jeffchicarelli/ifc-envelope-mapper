namespace IfcEnvelopeMapper.Core.Debug;

public readonly record struct DebugColor(float R, float G, float B)
{
    public static readonly DebugColor Original    = new(0.7f, 0.7f, 0.7f);
    public static readonly DebugColor Subject     = new(0.2f, 0.6f, 1.0f);
    public static readonly DebugColor GroundTruth = new(0.2f, 0.8f, 0.2f);
    public static readonly DebugColor Error       = new(1.0f, 0.2f, 0.2f);
    public static readonly DebugColor Warning     = new(1.0f, 0.8f, 0.0f);

    public static readonly DebugColor Exterior    = new(0.2f, 0.8f, 0.4f);
    public static readonly DebugColor Interior    = new(0.4f, 0.4f, 0.8f);
}
