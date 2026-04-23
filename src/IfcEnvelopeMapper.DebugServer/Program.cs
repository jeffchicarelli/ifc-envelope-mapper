using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;

// Helper process for the debug viewer. Runs in a separate OS process from
// the CLI so the .NET debugger — attached to the CLI for breakpoint work —
// has no authority to freeze this process's threads. See ADR-17 update
// block (2026-04-21) in docs/plano.md.
//
// Config handshake: the parent writes one JSON line to our stdin and closes
// the pipe. Replaces the old positional-args contract (4 CLI args in a fixed
// order) — a typed record survives renames and leaves room for new fields
// (extra sidecars, log level) without reshuffling args at every call site.

var configLine = Console.In.ReadLine();
if (string.IsNullOrWhiteSpace(configLine))
{
    Console.Error.WriteLine("DebugServer: expected JSON config on stdin, got empty input");
    return 2;
}

HelperConfig? config;
try
{
    config = JsonSerializer.Deserialize<HelperConfig>(configLine);
}
catch (JsonException ex)
{
    Console.Error.WriteLine($"DebugServer: invalid JSON config: {ex.Message}");
    return 2;
}

if (config is null || string.IsNullOrEmpty(config.ViewerHtmlPath) || string.IsNullOrEmpty(config.GlbPath))
{
    Console.Error.WriteLine("DebugServer: config missing required fields (viewerHtmlPath, glbPath)");
    return 2;
}

// Sidecar path derived from the GLB path — same folder, fixed name. Keeps
// the CLI↔helper contract small (no new field for every new file type the
// viewer eventually wants).
var occupantsPath = Path.Combine(Path.GetDirectoryName(config.GlbPath)!, "ifc-debug-occupants.json");

// Root for the viewer's static assets (JS modules, future CSS/images). The
// HTML entry point sits at the top of this tree — any relative request from
// inside it is resolved against this directory, with a canonical-path check
// below to block path traversal (..\ / symlinks pointing outside the tree).
var viewerAssetRoot = Path.GetFullPath(Path.GetDirectoryName(config.ViewerHtmlPath)!);

// One GUID per helper process, advertised on every response. The viewer
// compares it across polls; a change signals a brand-new debug session and
// triggers location.reload() even if the poll loop never observed a fetch
// failure during the old→new helper handoff.
var sessionId = Guid.NewGuid().ToString("N");

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
            var parent = Process.GetProcessById(config.ParentPid);
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

var (listener, boundPort) = TryBind(config.Port);
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
    _ = Task.Run(() => HandleAsync(ctx, config.ViewerHtmlPath, viewerAssetRoot, config.GlbPath, occupantsPath, sessionId));
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

static async Task HandleAsync(HttpListenerContext ctx, string viewerHtmlPath, string viewerAssetRoot, string glbPath, string occupantsPath, string sessionId)
{
    try
    {
        ctx.Response.Headers["X-Debug-Session"] = sessionId;
        var path = ctx.Request.Url?.AbsolutePath ?? "/";

        // Payload routes come first — these live in C:\temp, not under the
        // viewer asset root. An occupants request must not fall through to
        // the static-asset resolver and get a 404 for the wrong reason.
        if (path is "/" or "/index.html")
        {
            await ServeFileAsync(ctx, viewerHtmlPath, "text/html; charset=utf-8");
            return;
        }
        if (path == "/ifc-debug-output.glb")
        {
            await ServeFileAsync(ctx, glbPath, "model/gltf-binary");
            return;
        }
        if (path == "/ifc-debug-occupants.json")
        {
            await ServeFileAsync(ctx, occupantsPath, "application/json; charset=utf-8");
            return;
        }

        // Static assets: JS modules (and future CSS/images) living next to the
        // viewer HTML. Path.GetFullPath normalizes %-decoded input and resolves
        // any .. segments; we then require the result to stay under the asset
        // root, so ../../../Windows/System32/... attempts 404 instead of escaping.
        var relative = Uri.UnescapeDataString(path.TrimStart('/'));
        var candidate = Path.GetFullPath(Path.Combine(viewerAssetRoot, relative));
        if (candidate.StartsWith(viewerAssetRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && File.Exists(candidate))
        {
            await ServeFileAsync(ctx, candidate, GuessContentType(candidate));
            return;
        }

        ctx.Response.StatusCode = 404;
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

// Minimal content-type mapper — we only ship a handful of asset types. Anything
// unknown falls back to octet-stream, which the browser handles sensibly (and
// never gets served because the resolver only admits known extensions indirectly
// via File.Exists on the viewer asset tree).
static string GuessContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
{
    ".js"   => "text/javascript; charset=utf-8",
    ".mjs"  => "text/javascript; charset=utf-8",
    ".css"  => "text/css; charset=utf-8",
    ".html" => "text/html; charset=utf-8",
    ".json" => "application/json; charset=utf-8",
    ".svg"  => "image/svg+xml",
    ".png"  => "image/png",
    _       => "application/octet-stream",
};

static async Task ServeFileAsync(HttpListenerContext ctx, string path, string contentType)
{
    if (!File.Exists(path))
    {
        ctx.Response.StatusCode = 404;
        return;
    }

    // FileShare.ReadWrite | FileShare.Delete: the CLI atomically replaces the
    // GLB via write-tmp + File.Move(overwrite). File.ReadAllBytesAsync opens
    // with FileShare.Read (no Delete), so a concurrent rename is denied by
    // Windows and the CLI throws UnauthorizedAccessException. Opening with
    // Delete sharing lets the rename succeed — this handle keeps reading the
    // original file contents via its existing handle, and the next poll picks
    // up the new file.
    byte[] bytes;
    DateTime lastWriteUtc;
    await using (var fs = new FileStream(
        path, FileMode.Open, FileAccess.Read,
        FileShare.ReadWrite | FileShare.Delete))
    {
        // Read timestamp from the HANDLE, not the path. File.Move(overwrite:true)
        // swaps the inode at `path` atomically; a path-based timestamp lookup can
        // resolve to the NEW inode while `fs` is still pinned to the OLD one,
        // producing a response where bytes and Last-Modified come from different
        // file versions — the viewer would cache the new timestamp, see no change
        // on the next poll, and never refetch.
        lastWriteUtc = File.GetLastWriteTimeUtc(fs.SafeFileHandle);
        using var ms = new MemoryStream((int)fs.Length);
        await fs.CopyToAsync(ms);
        bytes = ms.ToArray();
    }

    ctx.Response.ContentType     = contentType;
    ctx.Response.ContentLength64 = bytes.Length;

    // Ticks (100 ns) instead of RFC 1123 ("R", 1 s) so two flushes in the same
    // calendar second produce distinct Last-Modified values. Viewer compares
    // this header as a plain string, not a date.
    ctx.Response.Headers["Last-Modified"] = lastWriteUtc.Ticks.ToString();
    ctx.Response.Headers["Cache-Control"] = "no-store";

    await ctx.Response.OutputStream.WriteAsync(bytes);
}

// Handshake payload. Public + record so the parent (DebugSession) can
// serialize the same shape without duplicating field names — System.Text.Json
// property-name matching is case-insensitive by default, but camelCase output
// keeps the on-wire JSON small and human-readable.
internal sealed record HelperConfig(
    [property: JsonPropertyName("port")]           int    Port,
    [property: JsonPropertyName("parentPid")]      int    ParentPid,
    [property: JsonPropertyName("viewerHtmlPath")] string ViewerHtmlPath,
    [property: JsonPropertyName("glbPath")]        string GlbPath);
