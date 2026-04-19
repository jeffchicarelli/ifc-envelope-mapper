namespace IfcEnvelopeMapper.Core.Debug;

public static class Debug
{
    private static IDebugSink _sink = NullDebugSink.Instance;

    public static void Configure(IDebugSink sink) => _sink = sink;

    public static void Emit(DebugShape shape) => _sink.Emit(shape);
    public static void Metric(string name, double value) => _sink.Metric(name, value);
    public static IDisposable BeginScope(string name) => _sink.BeginScope(name);
}
