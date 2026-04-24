namespace IfcEnvelopeMapper.Engine.Visualization;

// Atomic-write helpers shared by anything that wants to publish a file
// readers might be polling (debug GLB, voxel-occupancy sidecar). Used by
// both Debug/GltfSerializer and Geometry/Detection/SidecarWriter.
public static class AtomicFile
{
    // File.Move(overwrite:true) occasionally hits UnauthorizedAccessException
    // on Windows when something else holds the destination open without
    // FileShare.Delete: an orphan helper from a previous run still inside its
    // 2 s watchdog window, anti-virus mid-scan, or Google Drive Streaming's
    // filesystem shim (see ADR about running the CLI from C:\temp). All three
    // are transient — 20 x 50 ms = ~1 s total wait covers every occurrence
    // observed so far. A 1 s stall during a debug flush is still visible to
    // the user, so a real deadlock wouldn't hide here.
    public static void MoveWithRetry(string src, string dest)
    {
        const int attempts = 20;
        for (var i = 0; i < attempts; i++)
        {
            try
            {
                File.Move(src, dest, overwrite: true);
                return;
            }
            catch (UnauthorizedAccessException) when (i < attempts - 1)
            {
                Thread.Sleep(50);
            }
            catch (IOException) when (i < attempts - 1)
            {
                Thread.Sleep(50);
            }
        }
    }
}
