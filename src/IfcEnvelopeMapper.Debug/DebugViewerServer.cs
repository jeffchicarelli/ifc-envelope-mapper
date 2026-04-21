using System.Net;

namespace IfcEnvelopeMapper.Debug;

// Minimal HttpListener-based static server for the debug-viewer.
// Serves the viewer HTML at / and the current GLB at /ifc-debug-output.glb.
// Loopback-only binding avoids the Windows Defender firewall prompt.
// Lives in Geometry.Debug (not CLI) so test projects can start it from a fixture.
public sealed class DebugViewerServer : IDisposable
{
    private readonly HttpListener _listener;
    private readonly string _viewerHtmlPath;
    private readonly string _glbPath;
    private readonly CancellationTokenSource _cts = new();

    private DebugViewerServer(HttpListener listener, string viewerHtmlPath, string glbPath)
    {
        _listener       = listener;
        _viewerHtmlPath = viewerHtmlPath;
        _glbPath        = glbPath;
    }

    // Tries port first, falls back to an OS-assigned free port on conflict.
    public static DebugViewerServer Start(int preferredPort, string viewerHtmlPath, string glbPath)
    {
        var (listener, port) = TryBind(preferredPort);
        var server = new DebugViewerServer(listener, viewerHtmlPath, glbPath);
        _ = Task.Run(server.AcceptLoopAsync);
        Console.WriteLine($"Debug viewer: http://localhost:{port}/");
        return server;
    }

    private static (HttpListener listener, int port) TryBind(int preferredPort)
    {
        foreach (var port in new[] { preferredPort, 0 })
        {
            try
            {
                var actualPort = port == 0 ? GetFreePort() : port;
                var listener   = new HttpListener();
                listener.Prefixes.Add($"http://localhost:{actualPort}/");
                listener.Start();
                return (listener, actualPort);
            }
            catch (HttpListenerException) when (port != 0)
            {
                // preferred port busy — retry with OS-assigned port
            }
        }

        throw new InvalidOperationException("Could not bind debug viewer server");
    }

    private static int GetFreePort()
    {
        var s = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        s.Start();
        var port = ((IPEndPoint)s.LocalEndpoint).Port;
        s.Stop();
        return port;
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch (HttpListenerException) { return; }
            catch (ObjectDisposedException) { return; }

            _ = Task.Run(() => HandleAsync(ctx));
        }
    }

    private async Task HandleAsync(HttpListenerContext ctx)
    {
        try
        {
            var path = ctx.Request.Url?.AbsolutePath ?? "/";
            switch (path)
            {
                case "/":
                case "/index.html":
                    await ServeFileAsync(ctx, _viewerHtmlPath, "text/html; charset=utf-8");
                    break;
                case "/ifc-debug-output.glb":
                    await ServeFileAsync(ctx, _glbPath, "model/gltf-binary");
                    break;
                default:
                    ctx.Response.StatusCode = 404;
                    break;
            }
        }
        catch
        {
            try
            {
                ctx.Response.StatusCode = 500;
            } catch
            { /* ignore */
            }
        }
        finally
        {
            ctx.Response.Close();
        }
    }

    private static async Task ServeFileAsync(HttpListenerContext ctx, string path, string contentType)
    {
        if (!File.Exists(path))
        {
            ctx.Response.StatusCode = 404;
            return;
        }

        var bytes = await File.ReadAllBytesAsync(path);
        ctx.Response.ContentType   = contentType;
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.Headers["Last-Modified"] = File.GetLastWriteTimeUtc(path).ToString("R");
        ctx.Response.Headers["Cache-Control"] = "no-store";
        await ctx.Response.OutputStream.WriteAsync(bytes);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Close();
    }
}
