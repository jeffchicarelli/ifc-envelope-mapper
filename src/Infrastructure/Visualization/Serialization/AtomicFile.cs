namespace IfcEnvelopeMapper.Infrastructure.Visualization.Serialization;

/// <summary>
/// Atomic-write helpers shared by anything that wants to publish a file readers might be polling
/// (debug GLB, voxel-occupancy JSON). Used by <see cref="GltfSerializer"/> and <see cref="VoxelOccupants"/>.
/// </summary>
internal static class AtomicFile
{
    /// <summary>
    /// Moves <paramref name="src"/> to <paramref name="dest"/> with up to 20 retries at 50 ms intervals.
    /// <c>File.Move(overwrite:true)</c> occasionally hits transient errors on Windows:
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// <see cref="UnauthorizedAccessException"/>: something holds the destination open
    /// without <c>FileShare.Delete</c> (orphan helper inside 2 s watchdog window, anti-virus mid-scan,
    /// or Google Drive Streaming's filesystem shim).
    /// </description>
    /// </item>
    /// <item>
    /// <description><see cref="IOException"/>: same flavour from the GDrive shim.</description>
    /// </item>
    /// <item>
    /// <description>
    /// <see cref="FileNotFoundException"/>: the source <c>.tmp</c> vanished mid-flight.
    /// Treated as "we already lost this update, the next Flush will recreate" — returns silently because
    /// debug visualization is best-effort by design.
    /// </description>
    /// </item>
    /// </list>
    /// </summary>
    public static void MoveWithRetry(string src, string dest)
    {
        const int attempts = 20;

        for (var i = 0; i < attempts; i++)
        {
            try
            {
                File.Move(src, dest, true);

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
