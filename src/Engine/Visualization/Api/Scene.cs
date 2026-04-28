using SharpGLTF.Scenes;

using IfcEnvelopeMapper.Engine.Visualization.Serialization;

namespace IfcEnvelopeMapper.Engine.Visualization.Api;

// Per-flow scene state. AsyncLocal<State> gives each xunit test method
// (and the production CLI run) its own isolated SceneBuilder + output path
// — no per-test setup, no cross-test contamination. The OS-level helper
// process is owned by ViewerHelper, not here.
//
// Configure is required before any emission: a test that forgets it gets
// InvalidOperationException at the first Add, not a silent default that
// collides with other tests' GLB writes.
//
// Long-lived SceneBuilder: each Add appends a glTF mesh primitive to the
// SceneBuilder *immediately* (one-time AddTriangle iteration per shape),
// rather than accumulating raw shapes in a buffer to be re-encoded on every
// flush. Each flush just calls SaveGlb on the existing SceneBuilder.
// Per-flush cost drops from O(N²·triangles) (rebuild everything each time)
// to O(N·triangles) total across all flushes — the same memory profile a
// shape-cache would give, without SharpGLTF state-sharing bugs.
//
// Flush is automatic. During emission bursts Add throttles to at most one
// flush per FLUSH_THROTTLE_MS. After the last Add, an idle timer fires
// FLUSH_THROTTLE_MS later and writes any pending shapes — so a breakpoint
// set after the last emission sees up-to-date state in the viewer without
// any explicit Flush() call. GeometryDebug.Flush() is still available for
// callers that want to bypass the timer (asserting immediately after
// emission, etc.).
internal static class Scene
{
    // Coalesce rapid Adds inside a debounce window. Bigger = less encode
    // work during bursts AND a longer wait at breakpoints; smaller = the
    // viewer sees fresher state during step-by-step debugging. 100 ms is
    // below the viewer's poll cadence and below human reaction time, so
    // the trade-off is invisible at both ends.
    private const long FLUSH_THROTTLE_MS = 100;

    private sealed class State(string outputPath, bool launchServer)
    {
        // Per-State lock protects Builder mutations from racing with the
        // background idle-flush timer (which runs on a thread-pool thread).
        public readonly object Lock = new();
        // Long-lived SceneBuilder. Adds append to it; flushes serialise it.
        public SceneBuilder Builder { get; set; } = new();

        public string OutputPath     { get; set; } = outputPath;
        public bool   LaunchServer   { get; set; } = launchServer;
        public long   LastFlushTicks { get; set; }
        public bool   Dirty          { get; set; }
        public Timer? IdleFlushTimer { get; set; }
    }

    private static readonly AsyncLocal<State?> _state = new();

    // Serialises file writes across flows. Per-State Lock already isolates
    // in-memory mutations; this exists because the AtomicFile ".tmp + rename"
    // pattern races when two flows happen to share the same OutputPath
    // (defensive — required Configure makes the collision case unlikely).
    private static readonly object _flushLock = new();

    private static State Current
    {
        get
        {
            var state = _state.Value;
            if (state is null)
            {
                throw new InvalidOperationException(
                    "GeometryDebug.Configure must be called before any emission. " +
                    "Tests typically call Configure(outputPath, launchServer: false) at setup.");
            }
            return state;
        }
    }

    public static string OutputPath  => Current.OutputPath;
    public static bool   LaunchServer => Current.LaunchServer;

    public static void Configure(string outputPath, bool launchServer = true)
    {
        // Dispose any prior timer so its callback can't fire on stale state.
        _state.Value?.IdleFlushTimer?.Dispose();

        var state = new State(outputPath, launchServer);
        // Dormant timer; Add(...) reschedules it on each call.
        state.IdleFlushTimer = new Timer(IdleFlushCallback, state, Timeout.Infinite, Timeout.Infinite);
        _state.Value = state;
    }

    // Append shape to the long-lived SceneBuilder + maybe flush. Throttled
    // inside the burst window; outside the window, reschedules the idle
    // timer so a paused debugger / breakpoint observer sees the buffer on
    // disk shortly.
    public static void Add(DebugShape shape)
    {
        var state = Current;
        bool flushNow;
        lock (state.Lock)
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

    // Append without flushing. Use inside batch helpers that want to add
    // many shapes and trigger a single Flush at the end (avoids the
    // throttled flushes mid-loop).
    public static void AddNoFlush(DebugShape shape)
    {
        var state = Current;
        lock (state.Lock)
        {
            GltfSerializer.AddShape(state.Builder, shape);
            state.Dirty = true;
        }
    }

    // Force-flush the current SceneBuilder. Bypasses the throttle and the
    // idle timer. Use at the end of a batch helper, end of a test, or at
    // a breakpoint where waiting for the idle timer would be inconvenient.
    public static void Flush()
    {
        FlushInternal(Current);
    }

    public static void Clear()
    {
        var state = Current;
        lock (state.Lock)
        {
            // Replace the SceneBuilder with a fresh empty one — the previous
            // one (with all its accumulated meshes) becomes GC-eligible.
            state.Builder = new SceneBuilder();
            state.Dirty = true;
        }
        FlushInternal(state);
    }

    private static void FlushInternal(State state)
    {
        SceneBuilder snapshot;
        lock (state.Lock)
        {
            snapshot              = state.Builder;
            state.LastFlushTicks  = Environment.TickCount64;
            state.Dirty           = false;
        }
        lock (_flushLock)
        {
            GltfSerializer.SaveGlb(snapshot, state.OutputPath);
        }
        // Cancel any pending idle flush — we just flushed.
        state.IdleFlushTimer?.Change(Timeout.Infinite, Timeout.Infinite);
    }

    private static void IdleFlushCallback(object? stateObj)
    {
        var state = (State)stateObj!;
        bool needsFlush;
        lock (state.Lock)
        {
            needsFlush = state.Dirty;
        }
        if (needsFlush)
        {
            FlushInternal(state);
        }
    }
}
