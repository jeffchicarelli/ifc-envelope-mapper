using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

// Helper process for the debug viewer. Runs in a separate OS process from
// the CLI so the .NET debugger — attached to the CLI for breakpoint work —
// has no authority to freeze this process's threads. See ADR-17 update
// block (2026-04-21) in docs/plano.md.
//
// Args (positional, internal contract with GeometryDebug):
//   [0] port            preferred port (falls back to OS-assigned on conflict)
//   [1] parentPid       CLI's PID; helper self-exits if parent dies
//   [2] viewerHtmlPath  absolute path to debug-viewer/index.html
//   [3] glbPath         absolute path to ifc-debug-output.glb (may not exist yet)

if (args.Length < 4)
{
    Console.Error.WriteLine(
        "Usage: IfcEnvelopeMapper.DebugServer <port> <parentPid> <viewerHtmlPath> <glbPath>");
    return 2;
}

var preferredPort  = int.Parse(args[0]);
var parentPid      = int.Parse(args[1]);
var viewerHtmlPath = args[2];
var glbPath        = args[3];

// Watchdog. If the CLI dies without running its ProcessExit handler (hard
// crash, power loss, SIGKILL), this helper would otherwise outlive its
// purpose and keep the port bound. Poll every 2 s; self-exit when the
// parent PID is gone.
_ = Task.Run(async () =>
{
    while (true)
    {
        try
        {
            var parent = Process.GetProcessById(parentPid);
            if (parent.HasExited)
            {
                Environment.Exit(0);
            }
        }
        catch (ArgumentException)
        {
            // PID no longer exists in the process table.
            Environment.Exit(0);
        }

        await Task.Delay(2000);
    }
});

var (listener, boundPort) = TryBind(preferredPort);
Console.WriteLine($"Debug viewer: http://localhost:{boundPort}/");

while (true)
{
    HttpListenerContext ctx;
    try
    {
        ctx = await listener.GetContextAsync();
    }
    catch (HttpListenerException) { break; }
    catch (ObjectDisposedException) { break; }

    // Fire-and-forget: one Task per request. HttpListener can have many
    // concurrent in-flight requests (the viewer polls every 200 ms, but also
    // fetches HTML + GLB on load); serial handling would stall the poll loop.
    _ = Task.Run(() => HandleAsync(ctx, viewerHtmlPath, glbPath));
}

return 0;


static (HttpListener listener, int port) TryBind(int preferredPort)
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

static int GetFreePort()
{
    var s = new TcpListener(IPAddress.Loopback, 0);
    s.Start();
    var port = ((IPEndPoint)s.LocalEndpoint).Port;
    s.Stop();
    return port;
}

static async Task HandleAsync(HttpListenerContext ctx, string viewerHtmlPath, string glbPath)
{
    try
    {
        var path = ctx.Request.Url?.AbsolutePath ?? "/";
        switch (path)
        {
            case "/":
            case "/index.html":
                await ServeFileAsync(ctx, viewerHtmlPath, "text/html; charset=utf-8");
                break;
            case "/ifc-debug-output.glb":
                await ServeFileAsync(ctx, glbPath, "model/gltf-binary");
                break;
            default:
                ctx.Response.StatusCode = 404;
                break;
        }
    }
    catch
    {
        try { ctx.Response.StatusCode = 500; } catch { /* ignore */ }
    }
    finally
    {
        ctx.Response.Close();
    }
}

static async Task ServeFileAsync(HttpListenerContext ctx, string path, string contentType)
{
    if (!File.Exists(path))
    {
        ctx.Response.StatusCode = 404;
        return;
    }

    var bytes = await File.ReadAllBytesAsync(path);
    ctx.Response.ContentType     = contentType;
    ctx.Response.ContentLength64 = bytes.Length;

    // Ticks (100 ns) instead of RFC 1123 ("R", 1 s) so two flushes in the same
    // calendar second produce distinct Last-Modified values. Viewer compares
    // this header as a plain string, not a date.
    ctx.Response.Headers["Last-Modified"] = File.GetLastWriteTimeUtc(path).Ticks.ToString();
    ctx.Response.Headers["Cache-Control"] = "no-store";

    await ctx.Response.OutputStream.WriteAsync(bytes);
}
