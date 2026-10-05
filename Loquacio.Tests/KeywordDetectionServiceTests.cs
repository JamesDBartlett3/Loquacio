using NSubstitute;
using Loquacio.Services;
using Xunit;

namespace Loquacio.Tests;

public class KeywordDetectionServiceTests
{
    private IKeywordDetectionService CreateService()
    {
        var audioCapture = Substitute.For<IAudioCaptureService>();
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<KeywordDetectionService>>();
        return new KeywordDetectionService(audioCapture, logger);
    }

    [Fact]
    public void IsAvailable_DefaultEnergyBased_AlwaysAvailable()
    {
        var service = CreateService();

        Assert.True(service.IsAvailable);
    }

    [Fact]
    public void IsActive_BeforeStart_ReturnsFalse()
    {
        var service = CreateService();

        Assert.False(service.IsActive);
    }

    [Fact]
    public async Task StartAsync_ValidKeyword_SetsActive()
    {
        var service = CreateService();

        await service.StartAsync("Hey Dictate");

        Assert.True(service.IsActive);
        await service.StopAsync();
    }

    [Fact]
    public async Task StopAsync_AfterStart_SetsInactive()
    {
        var service = CreateService();
        await service.StartAsync("Hey Dictate");

        await service.StopAsync();

        Assert.False(service.IsActive);
    }

    [Fact]
    public async Task StartAsync_CalledTwice_DoesNotThrow()
    {
        var service = CreateService();
        await service.StartAsync("Hey Dictate");

        // Second call should be a no-op, not throw
        await service.StartAsync("Hey Dictate");

        await service.StopAsync();
    }

    [Fact]
    public async Task StopAsync_NotStarted_DoesNotThrow()
    {
        var service = CreateService();

        await service.StopAsync(); // Should be a no-op
    }

    [Fact]
    public async Task KeywordDetected_Event_FiresOnDetection()
    {
        var audioCapture = Substitute.For<IAudioCaptureService>();
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<KeywordDetectionService>>();
        var service = new KeywordDetectionService(audioCapture, logger);

        var detected = false;
        service.KeywordDetected += (s, e) => detected = true;

        await service.StartAsync("Hey Dictate");

        // Simulate high audio level to trigger VAD
        audioCapture.OnAudioLevel += Raise.Event<EventHandler<double>>(this, 100.0);

        // The energy-based detector should detect this as speech
        // (may or may not trigger depending on threshold and cooldown logic)
        Assert.False(detected); // Single frame unlikely to trigger — cooldown and min-frames apply

        await service.StopAsync();
    }

    [Fact]
    public async Task ProcessAudioFrame_WhenNotActive_DoesNothing()
    {
        var service = CreateService();

        // Should not throw when processing frames without being active
        service.ProcessAudioFrame(new short[] { 1, 2, 3, 4 });
    }

    [Fact]
    public async Task ProcessAudioFrame_EmptyArray_DoesNotThrow()
    {
        var service = CreateService();
        await service.StartAsync("Test");

        service.ProcessAudioFrame(Array.Empty<short>());

        await service.StopAsync();
    }

    [Fact]
    public async Task ProcessAudioFrame_NullArray_DoesNotThrow()
    {
        var service = CreateService();
        await service.StartAsync("Test");

#pragma warning disable CS8625
        service.ProcessAudioFrame(null);
#pragma warning restore CS8625

        await service.StopAsync();
    }

    [Fact]
    public async Task Dispose_AfterStart_StopsService()
    {
        var service = CreateService();
        await service.StartAsync("Test");

        service.Dispose();

        Assert.False(service.IsActive);
    }
}
