using System.Diagnostics;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using static IfcEnvelopeMapper.Core.Diagnostics.AppLog;

namespace IfcEnvelopeMapper.Engine.Visualization.Api;

// Process-wide singleton: owns the viewer helper process. Spawned once per
// .NET process via Interlocked.Exchange race; subsequent calls early-return.
// All stdout/stderr from the helper is pumped through AppLog so structured
// sinks see helper events alongside the rest of the application.
internal static class ViewerHelper
{
    // Spawn-once flag. Interlocked.Exchange returns the previous value; if 0,
    // this thread won the race and proceeds to spawn. All others early-return.
    // _serverStarted only flips to 1 when we actually launch — so a
    // launchServer:false caller (typical test) doesn't deny later launchServer:true
    // callers (typical CLI) the ability to spawn.
    private static int      _started;
    private static Process? _helperProcess;

    public static void EnsureStarted(string glbPath, bool launchServer)
    {
        if (!launchServer)
        {
            return;
        }

        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return;
        }

        StartHelperProcess(glbPath);
    }

    // Wrapped in try/catch (with filter): a failed helper launch shouldn't
    // take the ability to log shapes down with it. The GLB file is still
    // written either way. CLR-fatal exceptions (OOM/SOE) are excluded so
    // they propagate to the runtime instead of being swallowed.
    private static void StartHelperProcess(string glbPath)
    {
        try
        {
            var helperDll  = Path.Combine(AppContext.BaseDirectory, "IfcEnvelopeMapper.DebugServer.dll");
            var viewerHtml = Path.Combine(AppContext.BaseDirectory, "debug-viewer", "index.html");

            if (!File.Exists(helperDll) || !File.Exists(viewerHtml))
            {
                Log.LogWarning(
                    "[ViewerHelper] viewer skipped — missing helper or HTML in {BaseDirectory} (helperDll={HelperDllExists}, html={HtmlExists})",
                    AppContext.BaseDirectory, File.Exists(helperDll), File.Exists(viewerHtml));
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(glbPath)!);

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
                Log.LogError("[ViewerHelper] Process.Start returned null");
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
                glbPath        = glbPath,
            };
            _helperProcess.StandardInput.WriteLine(JsonSerializer.Serialize(config));
            _helperProcess.StandardInput.Close();

            // Pump helper stdout/stderr through AppLog so the "Debug viewer:
            // http://localhost:PORT/" line and any errors surface to the user
            // alongside the rest of the application's logging.
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

            // Best-effort cleanup: ProcessExit fires on clean shutdown only.
            // The helper has its own parent-PID watchdog (parentPid in the
            // stdin handshake) for crash / kill -9 cases this misses.
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
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
        {
            Log.LogError(ex, "[ViewerHelper] viewer start failed");
        }
    }
}
