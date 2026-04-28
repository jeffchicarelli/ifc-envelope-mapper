using IfcEnvelopeMapper.Infrastructure.Visualization.Serialization;
using SharpGLTF.Scenes;

namespace IfcEnvelopeMapper.Infrastructure.Visualization.Api;

/// <summary>
/// Per-flow scene state. <c>AsyncLocal&lt;State&gt;</c> gives each xunit test method (and the production CLI
/// run) its own isolated <c>SceneBuilder</c> and output path — no per-test setup, no cross-test contamination.
/// <see cref="Configure"/> must be called before any emission; omitting it throws
/// <see cref="InvalidOperationException"/> at the first <see cref="Add"/> call.
/// Each <see cref="Add"/> appends a glTF mesh primitive immediately (one-time AddTriangle iteration per shape);
/// each flush calls <c>SaveGlb</c> on the existing <c>SceneBuilder</c> — O(N·triangles) total, not O(N²·triangles).
/// Flush is automatic: emission bursts throttle to one flush per <c>FLUSH_THROTTLE_MS</c>; an idle timer fires
/// after the last emission so a breakpoint set afterwards sees up-to-date state in the viewer.
/// </summary>
internal static class Scene
{
    private const long FLUSH_THROTTLE_MS = 100;
    private static readonly AsyncLocal<State?> _state = new();
    private static readonly object _flushLock = new();

    private static State Current
    {
        get
        {
            var state = _state.Value;

            if (state is null)
            {
                throw new InvalidOperationException("GeometryDebug.Configure must be called before any emission. " +
                                                    "Tests typically call Configure(outputPath, launchServer: false) at setup.");
            }

            return state;
        }
    }

    public static string OutputPath => Current.OutputPath;
    public static bool LaunchServer => Current.LaunchServer;

    public static void Configure(string outputPath, bool launchServer = true)
    {
        _state.Value?.IdleFlushTimer?.Dispose();

        var state = new State(outputPath, launchServer);

        state.IdleFlushTimer = new Timer(IdleFlushCallback, state, Timeout.Infinite, Timeout.Infinite);

        _state.Value = state;
    }

    public static void Add(DebugShape shape)
    {
        var state = Current;
        bool flushNow;

        lock (state._lock)
        {
            GltfSerializer.AddShape(state.Builder, shape);

            state.Dirty = true;
            flushNow = Environment.TickCount64 - state.LastFlushTicks >= FLUSH_THROTTLE_MS;
        }

        if (flushNow)
        {
            FlushInternal(state);
        }
        else
        {
            state.IdleFlushTimer?.Change(FLUSH_THROTTLE_MS, Timeout.Infinite);
        }
    }

    public static void AddNoFlush(DebugShape shape)
    {
        var state = Current;

        lock (state._lock)
        {
            GltfSerializer.AddShape(state.Builder, shape);

            state.Dirty = true;
        }
    }

    public static void Flush()
    {
        FlushInternal(Current);
    }

    public static void Clear()
    {
        var state = Current;

        lock (state._lock)
        {
            state.Builder = new SceneBuilder();
            state.Dirty = true;
        }

        FlushInternal(state);
    }

    private static void FlushInternal(State state)
    {
        SceneBuilder snapshot;

        lock (state._lock)
        {
            snapshot = state.Builder;
            state.LastFlushTicks = Environment.TickCount64;
            state.Dirty = false;
        }

        lock (_flushLock)
        {
            GltfSerializer.SaveGlb(snapshot, state.OutputPath);
        }

        state.IdleFlushTimer?.Change(Timeout.Infinite, Timeout.Infinite);
    }

    private static void IdleFlushCallback(object? stateObj)
    {
        var state = (State)stateObj!;
        bool needsFlush;

        lock (state._lock)
        {
            needsFlush = state.Dirty;
        }

        if (needsFlush)
        {
            FlushInternal(state);
        }
    }

    private sealed class State(string outputPath, bool launchServer)
    {
        public readonly object _lock = new();
        public SceneBuilder Builder { get; set; } = new();

        public string OutputPath { get; } = outputPath;
        public bool LaunchServer { get; } = launchServer;
        public long LastFlushTicks { get; set; }
        public bool Dirty { get; set; }
        public Timer? IdleFlushTimer { get; set; }
    }
}
