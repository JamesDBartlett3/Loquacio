using WhisperDictation.Services;

namespace WhisperDictation.Tests;

public class SingleInstanceServiceTests
{
    [Fact]
    public void Constructor_DoesNotThrow()
    {
        using var service = new SingleInstanceService();
        Assert.NotNull(service);
    }

    [Fact]
    public void IsLockHeld_DefaultFalse()
    {
        using var service = new SingleInstanceService();
        Assert.False(service.IsLockHeld);
    }

    [Fact]
    public void TryAcquireLock_ReturnsTrue_FirstCall()
    {
        using var service = new SingleInstanceService();
        var result = service.TryAcquireLock();
        Assert.True(result);
        Assert.True(service.IsLockHeld);
    }

    [Fact]
    public void TryAcquireLock_SecondInstanceReturnsFalse()
    {
        using var first = new SingleInstanceService();
        Assert.True(first.TryAcquireLock());

        using var second = new SingleInstanceService();
        Assert.False(second.TryAcquireLock());
        Assert.False(second.IsLockHeld);
    }

    [Fact]
    public void Dispose_ReleasesLock()
    {
        var service = new SingleInstanceService();
        service.TryAcquireLock();
        Assert.True(service.IsLockHeld);

        service.Dispose();
        Assert.False(service.IsLockHeld);
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var service = new SingleInstanceService();
        service.TryAcquireLock();
        service.Dispose();
        service.Dispose(); // Should be safe
    }

    [Fact]
    public void TryAcquireLock_AfterDispose_CanReacquire()
    {
        var first = new SingleInstanceService();
        first.TryAcquireLock();
        first.Dispose();

        using var second = new SingleInstanceService();
        Assert.True(second.TryAcquireLock());
    }
}
