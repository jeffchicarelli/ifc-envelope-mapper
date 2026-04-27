using System.Diagnostics;
using System.Text.Json;

using IfcEnvelopeMapper.Engine.Debug.Serialization;

using Microsoft.Extensions.Logging;

using static IfcEnvelopeMapper.Core.Diagnostics.AppLog;

namespace IfcEnvelopeMapper.Engine.Debug.Api;

// Process + file lifecycle for a debug run. Internal: the public surface is
// GeometryDebug. Splitting this out keeps GeometryDebug focused on the
// [Conditional("DEBUG")] API and takes the helper-launch machinery — which
// is stateful, touches Process/AppDomain — out of the facade file.
//
// The mutable state (shape list + output path + launch flag) lives in an
// AsyncLocal<State> so each logical async context — the production CLI run,
// each xunit test method — sees its own instance. xunit awaits each test
// method as its own task, which roots a fresh AsyncLocal scope; the test
// class is reconstructed per method, so methods inside a class don't share
// either. No locks, no cross-test interference, no test-side setup. The
// OS-level helper process is the only thing kept process-wide.
internal static class DebugSession
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

    // Helper process is a single OS resource shared across all flows in this
    // .NET process. Interlocked.Exchange gives a thread-safe spawn-once with
    // no lock. _serverStarted only flips to 1 when we actually launch — so a
    // launchServer:false caller (typical test) doesn't deny later launchServer:true
    // callers (typical CLI) the ability to spawn.
    private static int      _serverStarted;
    private static Process? _helperProcess;

    // Serialises the file write inside Flush. AsyncLocal already isolates the
    // in-memory shape list per flow, so concurrent Adds don't corrupt each
    // other's lists; this lock exists purely because the underlying AtomicFile
    // ".tmp + rename" pattern races when two flows happen to share the same
    // OutputPath (typical when neither has called Configure). Held only for the
    // duration of the GLB write — microseconds — so contention is negligible.
    private static readonly object _flushLock = new();

    internal static string OutputPath => Current.OutputPath;

    // Tests call this before any GeometryDebug.* invocation to redirect output
    // to a per-test path and (typically) skip spawning the viewer helper. CLI
    // never needs this — the default path + helper launch are correct for it.
    public static void Configure(string outputPath, bool launchServer = true)
    {
        var state = Current;
        state.OutputPath   = outputPath;
        state.LaunchServer = launchServer;
    }

    public static void Add(DebugShape shape)
    {
        if (!GeometryDebug.Enabled)
        {
            return;
        }

        var state = Current;
        EnsureServerStarted(state.LaunchServer);
        state.Shapes.Add(shape);
        lock (_flushLock)
        {
            GltfSerializer.Flush(state.Shapes, state.OutputPath);
        }
    }

    public static void Clear()
    {
        if (!GeometryDebug.Enabled)
        {
            return;
        }

        var state = Current;
        EnsureServerStarted(state.LaunchServer);
        state.Shapes.Clear();
        lock (_flushLock)
        {
            GltfSerializer.Flush(state.Shapes, state.OutputPath);
        }
    }

    private static void EnsureServerStarted(bool launchServer)
    {
        if (!launchServer)
        {
            return;
        }

        // Interlocked.Exchange returns the previous value; if 0, this thread
        // won the race and proceeds to spawn. All others early-return.
        if (Interlocked.Exchange(ref _serverStarted, 1) != 0)
        {
            return;
        }

        StartHelperProcess();
    }

    // Wrapped in try/catch: a failed helper launch shouldn't take the ability
    // to log shapes down with it. The GLB file is still written either way.
    private static void StartHelperProcess()
    {
        try
        {
            var helperDll  = Path.Combine(AppContext.BaseDirectory, "IfcEnvelopeMapper.Engine.DebugServer.dll");
            var viewerHtml = Path.Combine(AppContext.BaseDirectory, "debug-viewer", "index.html");

            if (!File.Exists(helperDll) || !File.Exists(viewerHtml))
            {
                Log.LogWarning(
                    "[DebugSession] viewer skipped — missing helper or HTML next to Debug.dll (helperDll={HelperDllExists}, html={HtmlExists})",
                    File.Exists(helperDll), File.Exists(viewerHtml));
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(Current.OutputPath)!);

            var startInfo = new ProcessStartInfo
            {
                FileName               = "dotnet",
                UseShellExecute        = false,
                RedirectStandardInput  = true,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true,
            };
            startInfo.ArgumentList.Add(helperDll);

            _helperProcess = Process.Start(startInfo);
            if (_helperProcess is null)
            {
                Log.LogError("[DebugSession] Process.Start returned null");
                return;
            }

            // Config handshake: one JSON line on stdin, then close. Replaces the
            // old positional-args contract — easier to extend (new sidecar type,
            // log level, etc.) and easier to read in logs than `dotnet X.dll 5173
            // 12345 C:\... C:\...`. Property names are camelCase to match the
            // [JsonPropertyName] attributes on DebugServer's HelperConfig record.
            var config = new
            {
                port           = 5173,
                parentPid      = Environment.ProcessId,
                viewerHtmlPath = viewerHtml,
                glbPath        = Current.OutputPath,
            };
            _helperProcess.StandardInput.WriteLine(JsonSerializer.Serialize(config));
            _helperProcess.StandardInput.Close();

            // Pump helper stdout/stderr into this console so the "Debug viewer:
            // http://localhost:PORT/" line and any errors surface to the user.
            _helperProcess.OutputDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                {
                    Log.LogInformation("{HelperOut}", e.Data);
                }
            };
            _helperProcess.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                {
                    Log.LogError("{HelperErr}", e.Data);
                }
            };
            _helperProcess.BeginOutputReadLine();
            _helperProcess.BeginErrorReadLine();

            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                try
                {
                    if (_helperProcess is { HasExited: false })
                    {
                        _helperProcess.Kill();
                    }
                }
                catch
                {
                    /* best-effort cleanup */
                }
            };
        }
        catch (Exception ex)
        {
            Log.LogError(ex, "[DebugSession] viewer start failed");
        }
    }
}
