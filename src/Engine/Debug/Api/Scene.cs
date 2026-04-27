using IfcEnvelopeMapper.Engine.Debug.Serialization;

namespace IfcEnvelopeMapper.Engine.Debug.Api;

// Per-flow scene state. AsyncLocal<State> gives each xunit test method
// (and the production CLI run) its own isolated shape buffer + output
// path — no per-test setup, no cross-test contamination. The OS-level
// helper process is owned by ViewerHelper, not here.
//
// Configure is required before any emission: a test that forgets it
// gets InvalidOperationException at the first Add, not a silent default
// that collides with other tests' GLB writes.
internal static class Scene
{
    private sealed class State(string outputPath, bool launchServer)
    {
        public List<DebugShape> Shapes       { get; }      = new();
        public string           OutputPath   { get; set; } = outputPath;
        public bool             LaunchServer { get; set; } = launchServer;
    }

    private static readonly AsyncLocal<State?> _state = new();

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

    // Serialises the file write inside Flush. AsyncLocal already isolates
    // the in-memory shape list per flow, so concurrent Adds don't corrupt
    // each other's lists; this lock exists purely because the AtomicFile
    // ".tmp + rename" pattern races when two flows happen to share the
    // same OutputPath (defensive — required Configure makes the collision
    // case unlikely). Held only for the duration of the GLB write.
    private static readonly object _flushLock = new();

    public static string OutputPath  => Current.OutputPath;
    public static bool   LaunchServer => Current.LaunchServer;

    // Tests call this before any GeometryDebug.* invocation to redirect
    // output to a per-test path and (typically) skip spawning the viewer
    // helper process. The CLI never needs this — it sets
    // GeometryDebug.Enabled=false at startup instead.
    public static void Configure(string outputPath, bool launchServer = true)
    {
        _state.Value = new State(outputPath, launchServer);
    }

    public static void Add(DebugShape shape)
    {
        var state = Current;
        state.Shapes.Add(shape);
        lock (_flushLock)
        {
            GltfSerializer.Flush(state.Shapes, state.OutputPath);
        }
    }

    public static void Clear()
    {
        var state = Current;
        state.Shapes.Clear();
        lock (_flushLock)
        {
            GltfSerializer.Flush(state.Shapes, state.OutputPath);
        }
    }
}
