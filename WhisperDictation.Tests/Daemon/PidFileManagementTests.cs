using WhisperDictation.Daemon.Services;

namespace WhisperDictation.Tests.Daemon;

/// <summary>
/// Tests for PID file management logic used by the daemon for single-instance enforcement.
/// Tests the same algorithm as Program.cs but against temp files.
/// </summary>
public class PidFileManagementTests : IDisposable
{
    private string _tempDir = null!;

    public PidFileManagementTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"whisper-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void TryAcquirePidLock_NewFile_Succeeds()
    {
        var pidPath = Path.Combine(_tempDir, "daemon.pid");

        var result = TryAcquirePidLock(pidPath, out var stream);

        Assert.True(result);
        Assert.NotNull(stream);
        Assert.True(File.Exists(pidPath));

        stream!.Dispose();
        TryReleasePidLock(pidPath, stream);
    }

    [Fact]
    public void TryAcquirePidLock_SecondAttempt_Fails()
    {
        var pidPath = Path.Combine(_tempDir, "daemon.pid");

        TryAcquirePidLock(pidPath, out var firstStream);
        var secondResult = TryAcquirePidLock(pidPath, out var secondStream);

        Assert.False(secondResult);
        Assert.Null(secondStream);

        TryReleasePidLock(pidPath, firstStream);
    }

    [Fact]
    public void TryAcquirePidLock_StaleFile_GetsReplaced()
    {
        var pidPath = Path.Combine(_tempDir, "daemon_stale.pid");

        // Write a stale PID file pointing to a non-existent process
        var unlikelyPid = int.MaxValue;
        File.WriteAllText(pidPath, unlikelyPid.ToString());

        // Small delay to ensure file handle is released by the OS
        Thread.Sleep(50);

        var result = TryAcquirePidLock(pidPath, out var stream);

        Assert.True(result);
        Assert.NotNull(stream);

        // Verify the file now contains our actual PID
        // Release first so we can read
        stream!.Dispose();
        var content = File.ReadAllText(pidPath).Trim();
        Assert.Equal(Environment.ProcessId.ToString(), content);

        TryReleasePidLock(pidPath, null);
    }

    [Fact]
    public void TryReleasePidLock_DeletesFile()
    {
        var pidPath = Path.Combine(_tempDir, "daemon.pid");

        TryAcquirePidLock(pidPath, out var stream);
        Assert.True(File.Exists(pidPath));

        TryReleasePidLock(pidPath, stream);

        Assert.False(File.Exists(pidPath));
    }

    [Fact]
    public void TryReleasePidLock_NullStream_DoesNotThrow()
    {
        var pidPath = Path.Combine(_tempDir, "daemon.pid");

        // Should not throw when stream is null
        TryReleasePidLock(pidPath, null);
    }

    // ── Replicas of Program.cs private methods for testing ──

    private static bool TryAcquirePidLock(string path, out FileStream? stream)
    {
        try
        {
            stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(stream, leaveOpen: true);
            writer.WriteLine(Environment.ProcessId);
            writer.Flush();
            return true;
        }
        catch (IOException)
        {
            try
            {
                var existingPid = int.Parse(File.ReadAllText(path).Trim());
                if (IsProcessAlive(existingPid))
                {
                    stream = null;
                    return false;
                }
                File.Delete(path);
                stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                using var writer = new StreamWriter(stream, leaveOpen: true);
                writer.WriteLine(Environment.ProcessId);
                writer.Flush();
                return true;
            }
            catch
            {
                stream = null;
                return false;
            }
        }
    }

    private static void TryReleasePidLock(string path, FileStream? stream)
    {
        stream?.Dispose();
        try { File.Delete(path); } catch { }
    }

    private static bool IsProcessAlive(int pid)
    {
        try
        {
            var proc = System.Diagnostics.Process.GetProcessById(pid);
            return !proc.HasExited;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return true;
        }
        catch (System.ArgumentException)
        {
            return false;
        }
    }
}
