using System.Diagnostics;
using System.Text.Json;

namespace IfcEnvelopeMapper.Debug;

// Process + file lifecycle for a debug run. Internal: the public surface is
// GeometryDebug. Splitting this out keeps GeometryDebug focused on the
// [Conditional("DEBUG")] API and takes the helper-launch machinery — which
// is stateful, touches Process/AppDomain — out of the facade file.
//
// One instance per AppDomain (static). First touch from any GeometryDebug.*
// call runs the static constructor, which spawns the out-of-process viewer
// server and registers a ProcessExit handler to kill it on clean shutdown.
// A parent-PID watchdog inside the helper (see DebugServer/Program.cs) is
// the backstop for hard crashes where ProcessExit never fires.
internal static class DebugSession
{
    // %TEMP% (AppData\Local\Temp) is blocked by Chromium's File System Access
    // API as a "system folder" so the file-picker-based fallback cannot read
    // it. C:\temp is also where the CLI runs from (Google Drive Streaming
    // native-DLL workaround), so everything lives in the same folder.
    internal static readonly string OutputPath =
        Path.Combine(@"C:\temp", "ifc-debug-output.glb");

    // Accumulates across the whole run — each GeometryDebug.* call appends
    // then re-flushes the full list. Not thread-safe; Detect is single-threaded.
    private static readonly List<DebugShape> _shapes = new();

    // Handle to the out-of-process HTTP viewer server. Kept at type scope so
    // the ProcessExit handler can terminate it on clean shutdown.
    private static Process? _helperProcess;

    // Static constructor runs once per AppDomain on first member touch.
    //
    // No Debugger.IsAttached gate: [Conditional("DEBUG")] on every public
    // GeometryDebug.* method already ensures Release builds have zero call
    // sites, so this static ctor never fires and no helper is spawned.
    //
    // Wrapped in try/catch: type-initializer exceptions are sticky — they
    // permanently brick the type for the process lifetime. A failed helper
    // launch shouldn't take the ability to log shapes down with it.
    static DebugSession()
    {
        try
        {
            var helperDll  = Path.Combine(AppContext.BaseDirectory, "IfcEnvelopeMapper.DebugServer.dll");
            var viewerHtml = Path.Combine(AppContext.BaseDirectory, "debug-viewer", "index.html");

            if (!File.Exists(helperDll) || !File.Exists(viewerHtml))
            {
                Console.Error.WriteLine(
                    $"[DebugSession] viewer skipped — missing helper or HTML next to Debug.dll " +
                    $"(helperDll={File.Exists(helperDll)}, html={File.Exists(viewerHtml)})");
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath)!);

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
                glbPath        = OutputPath,
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

    public static void Add(DebugShape shape)
    {
        _shapes.Add(shape);
        GltfSerializer.Flush(_shapes, OutputPath);
    }

    public static void Clear()
    {
        _shapes.Clear();
        GltfSerializer.Flush(_shapes, OutputPath);
    }
}
