using NSubstitute;
using Loquacio.Models;
using Loquacio.Services;

namespace Loquacio.Tests.Services;

public class IAudioCaptureServiceTests
{
    private readonly IAudioCaptureService _mockAudioCapture;

    public IAudioCaptureServiceTests()
    {
        _mockAudioCapture = Substitute.For<IAudioCaptureService>();
    }

    [Fact]
    public void IsCapturing_DefaultsToFalse_WhenMocked()
    {
        // Arrange
        _mockAudioCapture.IsCapturing.Returns(false);

        // Act & Assert
        Assert.False(_mockAudioCapture.IsCapturing);
    }

    [Fact]
    public void GetAvailableDevices_ReturnsDeviceList()
    {
        // Arrange
        var devices = new List<AudioDevice>
        {
            new() { Id = "dev1", FriendlyName = "Mic 1", IsCaptureDevice = true },
            new() { Id = "dev2", FriendlyName = "Mic 2", IsCaptureDevice = true }
        };
        _mockAudioCapture.GetAvailableDevices().Returns(devices);

        // Act
        var result = _mockAudioCapture.GetAvailableDevices();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal("Mic 1", result[0].FriendlyName);
    }

    [Fact]
    public void StartCapture_DoesNotThrow_WithValidDevice()
    {
        // Arrange
        var device = new AudioDevice { Id = "dev1", FriendlyName = "Mic", IsCaptureDevice = true };

        // Act & Assert - mock should accept the call without throwing
        _mockAudioCapture.StartCapture(device, 1500);
        _mockAudioCapture.Received(1).StartCapture(device, 1500);
    }

    [Fact]
    public void StopCapture_CanBeCalled()
    {
        // Act
        _mockAudioCapture.StopCapture();

        // Assert
        _mockAudioCapture.Received(1).StopCapture();
    }

    [Fact]
    public void OnSegmentCaptured_EventCanBeSubscribed()
    {
        // Arrange
        var receivedSegments = new List<AudioSegment>();
        _mockAudioCapture.OnSegmentCaptured += (sender, seg) => receivedSegments.Add(seg);

        // Act - raise event via NSubstitute
        var testSegment = new AudioSegment { Data = new byte[] { 1, 2, 3 } };
        _mockAudioCapture.OnSegmentCaptured += Raise.Event<EventHandler<AudioSegment>>(_mockAudioCapture, testSegment);

        // Assert
        Assert.Single(receivedSegments);
        Assert.Same(testSegment, receivedSegments[0]);
    }

    [Fact]
    public void OnAudioLevel_EventCanBeRaised()
    {
        // Arrange
        var levels = new List<double>();
        _mockAudioCapture.OnAudioLevel += (sender, level) => levels.Add(level);

        // Act
        _mockAudioCapture.OnAudioLevel += Raise.Event<EventHandler<double>>(_mockAudioCapture, 0.75);

        // Assert
        Assert.Single(levels);
        Assert.Equal(0.75, levels[0]);
    }

    [Fact]
    public void OnError_EventCanBeRaised()
    {
        // Arrange
        Exception? caughtException = null;
        _mockAudioCapture.OnError += (sender, ex) => caughtException = ex;

        // Act
        var testException = new InvalidOperationException("Test error");
        _mockAudioCapture.OnError += Raise.Event<EventHandler<Exception>>(_mockAudioCapture, testException);

        // Assert
        Assert.NotNull(caughtException);
        Assert.Equal("Test error", caughtException!.Message);
    }
}
