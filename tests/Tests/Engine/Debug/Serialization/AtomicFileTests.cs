using IfcEnvelopeMapper.Engine.Debug.Serialization;

namespace IfcEnvelopeMapper.Tests.Engine.Debug.Serialization;

/// <summary>
/// Unit tests for <see cref="AtomicFile.MoveWithRetry"/>. The retry/backoff
/// behaviour for transient locks is hard to exercise deterministically without
/// platform-specific filesystem mocks, so these tests cover the externally
/// visible contracts: happy path, dest overwrite, and missing source as a
/// silent no-op (best-effort guarantee documented in the source).
/// </summary>
public sealed class AtomicFileTests : IDisposable
{
    private readonly List<string> _tempPaths = new();

    [Fact]
    public void MoveWithRetry_HappyPath_RenamesAndCopiesContent()
    {
        var src  = NewTempFile(content: "payload-A");
        var dest = NewTempPath();

        AtomicFile.MoveWithRetry(src, dest);

        File.Exists(src).Should().BeFalse("source is consumed by Move");
        File.Exists(dest).Should().BeTrue();
        File.ReadAllText(dest).Should().Be("payload-A");
    }

    [Fact]
    public void MoveWithRetry_DestExists_OverwritesContent()
    {
        var src  = NewTempFile(content: "new-content");
        var dest = NewTempFile(content: "old-content");

        AtomicFile.MoveWithRetry(src, dest);

        File.ReadAllText(dest).Should().Be("new-content");
        File.Exists(src).Should().BeFalse();
    }

    [Fact]
    public void MoveWithRetry_MissingSource_IsSilentNoop()
    {
        // Documented behaviour: a vanished source means another writer already
        // landed something — return silently rather than crash. Debug
        // visualization is best-effort.
        var src  = Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}.tmp");
        var dest = NewTempPath();
        File.Exists(src).Should().BeFalse();

        var act = () => AtomicFile.MoveWithRetry(src, dest);

        act.Should().NotThrow();
        File.Exists(dest).Should().BeFalse();
    }

    [Fact]
    public void MoveWithRetry_BinaryPayload_PreservesBytes()
    {
        // Real callers (GltfSerializer) write GLB binary, not text. Confirm
        // the move path is byte-faithful for non-UTF-8 content.
        var bytes = new byte[] { 0x00, 0xFF, 0x10, 0xAB, 0xCD, 0xEF, 0x42 };
        var src   = NewTempPath();
        File.WriteAllBytes(src, bytes);
        var dest = NewTempPath();

        AtomicFile.MoveWithRetry(src, dest);

        File.ReadAllBytes(dest).Should().Equal(bytes);
    }

    private string NewTempFile(string content)
    {
        var path = NewTempPath();
        File.WriteAllText(path, content);
        return path;
    }

    private string NewTempPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"atomic-test-{Guid.NewGuid():N}.tmp");
        _tempPaths.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var path in _tempPaths)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
                /* best-effort cleanup */
            }
        }
    }
}
