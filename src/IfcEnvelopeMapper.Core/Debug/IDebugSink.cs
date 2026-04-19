namespace IfcEnvelopeMapper.Core.Debug;

public interface IDebugSink
{
    void Emit(DebugShape shape);
    void Metric(string name, double value);
    IDisposable BeginScope(string name);
}
