namespace IfcEnvelopeMapper.Engine.Debug;

// Atomic-write helpers shared by anything that wants to publish a file
// readers might be polling (debug GLB, voxel-occupancy sidecar). Used by
// GltfSerializer and SidecarWriter.
public static class AtomicFile
{
    // File.Move(overwrite:true) occasionally hits transient errors on Windows:
    //   - UnauthorizedAccessException: something holds the destination open
    //     without FileShare.Delete (orphan helper inside 2 s watchdog window,
    //     anti-virus mid-scan, or Google Drive Streaming's filesystem shim).
    //   - IOException: same flavour from the GDrive shim.
    //   - FileNotFoundException: the source `.tmp` vanished mid-flight — a
    //     concurrent Flush from another thread or test raced us and replaced
    //     the temp file before we could move it.
    // The first two get 20 x 50 ms = ~1 s of retry, which covers every case
    // observed so far. A FileNotFoundException is treated as "we already lost
    // this update, the next Flush will recreate" — return silently because
    // debug visualization is best-effort by design.
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
            catch (FileNotFoundException)
            {
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
