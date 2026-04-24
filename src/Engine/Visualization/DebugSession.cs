using System.Diagnostics;
using System.Text.Json;

namespace IfcEnvelopeMapper.Engine.Visualization;

// Process + file lifecycle for a debug run. Internal: the public surface is
// GeometryDebug. Splitting this out keeps GeometryDebug focused on the
// [Conditional("DEBUG")] API and takes the helper-launch machinery — which
// is stateful, touches Process/AppDomain — out of the facade file.
//
// One instance per AppDomain (static). The helper process is spawned LAZILY
// on the first Add()/Clear() call, not in the static ctor. This gives tests a
// chance to call Configure(path, launchServer:false) first to redirect output
// to a per-test path AND opt out of the helper-process spawn (no port collision,
// no orphaned dotnet processes when the test runner forks).
//
// CLI keeps the default behaviour: first GeometryDebug.* call spawns the helper,
// which serves the viewer on :5173 and watches the GLB for changes. A
// parent-PID watchdog inside the helper (see DebugServer/Program.cs) is the
// backstop for hard crashes where ProcessExit never fires.
internal static class DebugSession
{
    // %TEMP% (AppData\Local\Temp) is blocked by Chromium's File System Access
    // API as a "system folder" so the file-picker-based fallback cannot read
    // it. C:\temp is also where the CLI runs from (Google Drive Streaming
    // native-DLL workaround), so everything lives in the same folder.
    private static string _outputPath = Path.Combine(@"C:\temp", "ifc-debug-output.glb");
    private static bool _launchServer = true;
    private static bool _serverStarted = false;

    // Accumulates across the whole run — each GeometryDebug.* call appends
    // then re-flushes the full list. Not thread-safe; Detect is single-threaded.
    private static readonly List<DebugShape> _shapes = new();

    // Handle to the out-of-process HTTP viewer server. Kept at type scope so
    // the ProcessExit handler can terminate it on clean shutdown.
    private static Process? _helperProcess;

    internal static string OutputPath => _outputPath;

    // Tests call this before any GeometryDebug.* invocation to redirect output
    // to a per-test path and (typically) skip spawning the viewer helper. CLI
    // never calls this — the default path + helper launch are correct for it.
    public static void Configure(string outputPath, bool launchServer = true)
    {
        _outputPath = outputPath;
        _launchServer = launchServer;
    }

    public static void Add(DebugShape shape)
    {
        EnsureServerStarted();
        _shapes.Add(shape);
        GltfSerializer.Flush(_shapes, _outputPath);
    }

    public static void Clear()
    {
        EnsureServerStarted();
        _shapes.Clear();
        GltfSerializer.Flush(_shapes, _outputPath);
    }

    private static void EnsureServerStarted()
    {
        if (_serverStarted || !_launchServer) return;
        _serverStarted = true;
        StartHelperProcess();
    }

    // Wrapped in try/catch: a failed helper launch shouldn't take the ability
    // to log shapes down with it. The GLB file is still written either way.
    private static void StartHelperProcess()
    {
        try
        {
            var helperDll  = Path.Combine(AppContext.BaseDirectory, "IfcEnvelopeMapper.Engine.VisualizationServer.dll");
            var viewerHtml = Path.Combine(AppContext.BaseDirectory, "debug-viewer", "index.html");

            if (!File.Exists(helperDll) || !File.Exists(viewerHtml))
            {
                Console.Error.WriteLine(
                    $"[DebugSession] viewer skipped — missing helper or HTML next to Debug.dll " +
                    $"(helperDll={File.Exists(helperDll)}, html={File.Exists(viewerHtml)})");
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_outputPath)!);

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
                Console.Error.WriteLine("[DebugSession] Process.Start returned null");
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
                glbPath        = _outputPath,
            };
            _helperProcess.StandardInput.WriteLine(JsonSerializer.Serialize(config));
            _helperProcess.StandardInput.Close();

            // Pump helper stdout/stderr into this console so the "Debug viewer:
            // http://localhost:PORT/" line and any errors surface to the user.
            _helperProcess.OutputDataReceived += (_, e) => { if (e.Data is not null) Console.WriteLine(e.Data); };
            _helperProcess.ErrorDataReceived  += (_, e) => { if (e.Data is not null) Console.Error.WriteLine(e.Data); };
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
                catch { /* best-effort cleanup */ }
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[DebugSession] viewer start failed: {ex.Message}");
        }
    }
}
