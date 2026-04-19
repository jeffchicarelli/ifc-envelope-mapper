using g4;
using IfcEnvelopeMapper.Core.Debug;

namespace IfcEnvelopeMapper.Tests.Debug;

public sealed class DebugTests : IDisposable
{
    // Reset the static sink after each test to avoid cross-test interference.
    public void Dispose() => Core.Debug.Debug.Configure(NullDebugSink.Instance);

    // --- NullDebugSink ---

    [Fact]
    public void NullDebugSink_Emit_DoesNotThrow()
    {
        var sink = NullDebugSink.Instance;
        var shape = new DebugSphere(Vector3d.Zero, radius: 1.0) { Color = DebugColor.Subject };

        var act = () => sink.Emit(shape);

        act.Should().NotThrow();
    }

    [Fact]
    public void NullDebugSink_Metric_DoesNotThrow()
    {
        var sink = NullDebugSink.Instance;

        var act = () => sink.Metric("voxelCount", 1024);

        act.Should().NotThrow();
    }

    [Fact]
    public void NullDebugSink_BeginScope_ReturnsDisposable()
    {
        var sink = NullDebugSink.Instance;

        var scope = sink.BeginScope("grow-exterior");

        scope.Should().NotBeNull();
        var dispose = () => scope.Dispose();
        dispose.Should().NotThrow();
    }

    // --- Debug static facade ---

    [Fact]
    public void Debug_DefaultSink_EmitDoesNotThrow()
    {
        var shape = new DebugMesh(new DMesh3()) { Color = DebugColor.Original };

        var act = () => Core.Debug.Debug.Emit(shape);

        act.Should().NotThrow();
    }

    [Fact]
    public void Debug_Configure_DelegatesToConfiguredSink()
    {
        var spy = new SpyDebugSink();
        var shape = new DebugSphere(Vector3d.Zero, radius: 1.0) { Color = DebugColor.Exterior };

        Core.Debug.Debug.Configure(spy);
        Core.Debug.Debug.Emit(shape);

        spy.EmittedShapes.Should().ContainSingle().Which.Should().BeSameAs(shape);
    }

    [Fact]
    public void Debug_Metric_DelegatesToConfiguredSink()
    {
        var spy = new SpyDebugSink();

        Core.Debug.Debug.Configure(spy);
        Core.Debug.Debug.Metric("f1", 0.82);

        spy.Metrics.Should().ContainSingle()
            .Which.Should().Be(("f1", 0.82));
    }

    [Fact]
    public void Debug_BeginScope_DelegatesToConfiguredSink()
    {
        var spy = new SpyDebugSink();

        Core.Debug.Debug.Configure(spy);
        using (Core.Debug.Debug.BeginScope("classify")) { }

        spy.Scopes.Should().ContainSingle().Which.Should().Be("classify");
    }

    // --- SpyDebugSink ---

    private sealed class SpyDebugSink : IDebugSink
    {
        public List<DebugShape> EmittedShapes { get; } = [];
        public List<(string Name, double Value)> Metrics { get; } = [];
        public List<string> Scopes { get; } = [];

        public void Emit(DebugShape shape) => EmittedShapes.Add(shape);
        public void Metric(string name, double value) => Metrics.Add((name, value));
        public IDisposable BeginScope(string name)
        {
            Scopes.Add(name);
            return NullDebugSink.Instance.BeginScope(name);
        }
    }
}
