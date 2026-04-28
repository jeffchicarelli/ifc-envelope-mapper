using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using static IfcEnvelopeMapper.Infrastructure.Diagnostics.AppLog;

namespace IfcEnvelopeMapper.Infrastructure.Visualization.Api;

/// <summary>
/// Process-wide singleton that owns the viewer helper process. Spawned once per .NET process via an
/// <see cref="Interlocked"/> exchange race; subsequent calls return immediately. All stdout/stderr from
/// the helper is routed through <c>AppLog</c> so structured sinks see helper events alongside the application.
/// </summary>
internal static class ViewerHelper
{
    private static int _started;
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

    private static void StartHelperProcess(string glbPath)
    {
        try
        {
            var helperDll = Path.Combine(AppContext.BaseDirectory, "IfcEnvelopeMapper.DebugServer.dll");
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
                FileName = "dotnet",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
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
            var config = new { port = 5173, parentPid = Environment.ProcessId, viewerHtmlPath = viewerHtml, glbPath };
            _helperProcess.StandardInput.WriteLine(JsonSerializer.Serialize(config));
            _helperProcess.StandardInput.Close();

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
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
        {
            Log.LogError(ex, "[ViewerHelper] viewer start failed");
        }
    }
}
