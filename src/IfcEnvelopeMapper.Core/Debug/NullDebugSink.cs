namespace IfcEnvelopeMapper.Core.Debug;

public sealed class NullDebugSink : IDebugSink
{
    public static readonly NullDebugSink Instance = new();

    private NullDebugSink() { }

    public void Emit(DebugShape shape) { }
    public void Metric(string name, double value) { }
    public IDisposable BeginScope(string name) => NullScope.Instance;

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
