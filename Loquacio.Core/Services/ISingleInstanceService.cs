using System.Threading;

namespace Loquacio.Services;

/// <summary>
/// Enforces single-instance behavior for the application.
/// Uses a named Mutex to prevent multiple instances from running simultaneously.
/// </summary>
public interface ISingleInstanceService : IDisposable
{
    /// <summary>
    /// Attempts to acquire the single-instance lock.
    /// </summary>
    /// <returns>True if this is the first instance; false if another instance is already running.</returns>
    bool TryAcquireLock();

    /// <summary>
    /// Whether this instance holds the lock.
    /// </summary>
    bool IsLockHeld { get; }
}

/// <summary>
/// Default implementation using a named Mutex.
/// On Windows, the mutex name is prefixed with "Local\\" to ensure per-session scope.
/// </summary>
public class SingleInstanceService : ISingleInstanceService
{
    private const string MutexName = @"Local\Loquacio_SingleInstance";
    private Mutex? _mutex;
    private bool _lockHeld;

    public bool IsLockHeld => _lockHeld;

    public bool TryAcquireLock()
    {
        _mutex = new Mutex(initiallyOwned: true, name: MutexName, createdNew: out bool createdNew);

        if (createdNew)
        {
            _lockHeld = true;
            return true;
        }

        // Another instance already holds the mutex
        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // This thread doesn't own the mutex; ignore
        }

        _mutex.Dispose();
        _mutex = null;
        _lockHeld = false;
        return false;
    }

    public void Dispose()
    {
        if (!_lockHeld) return;

        try
        {
            _mutex?.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Thread doesn't own the mutex; ignore
        }

        _mutex?.Dispose();
        _mutex = null;
        _lockHeld = false;
        GC.SuppressFinalize(this);
    }
}
