using IfcEnvelopeMapper.Engine.Debug.Serialization;

namespace IfcEnvelopeMapper.Engine.Debug.Api;

// Per-flow scene state. AsyncLocal<State> gives each xunit test method
// (and the production CLI run) its own isolated shape buffer + output
// path — no per-test setup, no cross-test contamination. The OS-level
// helper process is owned by ViewerHelper, not here.
internal static class Scene
{
    private sealed class State
    {
        public List<DebugShape> Shapes       { get; }      = new();
        public string           OutputPath   { get; set; } = DEFAULT_OUTPUT_PATH;
        public bool             LaunchServer { get; set; } = true;
    }

    // %TEMP% (AppData\Local\Temp) is blocked by Chromium's File System Access
    // API as a "system folder" so the file-picker-based fallback cannot read
    // it. C:\temp is also where the CLI runs from (Google Drive Streaming
    // native-DLL workaround), so everything lives in the same folder.
    private static readonly string DEFAULT_OUTPUT_PATH =
        Path.Combine(@"C:\temp", "ifc-debug-output.glb");

    private static readonly AsyncLocal<State?> _state = new();
    private static State Current => _state.Value ??= new State();

    // Serialises the file write inside Flush. AsyncLocal already isolates
    // the in-memory shape list per flow, so concurrent Adds don't corrupt
    // each other's lists; this lock exists purely because the AtomicFile
    // ".tmp + rename" pattern races when two flows happen to share the
    // same OutputPath (typical when neither has called Configure). Held
    // only for the duration of the GLB write — microseconds — so contention
    // is negligible.
    private static readonly object _flushLock = new();

    public static string OutputPath  => Current.OutputPath;
    public static bool   LaunchServer => Current.LaunchServer;

    // Tests call this before any GeometryDebug.* invocation to redirect
    // output to a per-test path and (typically) skip spawning the viewer
    // helper process. The CLI never needs this — it sets
    // GeometryDebug.Enabled=false at startup instead.
    public static void Configure(string outputPath, bool launchServer = true)
    {
        var state = Current;
        state.OutputPath   = outputPath;
        state.LaunchServer = launchServer;
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
