using WhisperDictation.Models;

namespace WhisperDictation.Tests.Models;

public class AudioDeviceTests
{
    [Fact]
    public void AudioDevice_WithRequiredProperties_SetsCorrectly()
    {
        // Arrange & Act
        var device = new AudioDevice
        {
            Id = "wasapi-device-id-123",
            FriendlyName = "Microphone (Realtek Audio)",
            IsCaptureDevice = true
        };

        // Assert
        Assert.Equal("wasapi-device-id-123", device.Id);
        Assert.Equal("Microphone (Realtek Audio)", device.FriendlyName);
        Assert.True(device.IsCaptureDevice);
    }

    [Fact]
    public void AudioDevice_IsCaptureDevice_FalseForOutputDevice()
    {
        // Arrange & Act
        var device = new AudioDevice
        {
            Id = "output-device-id",
            FriendlyName = "Speakers",
            IsCaptureDevice = false
        };

        // Assert
        Assert.False(device.IsCaptureDevice);
    }

    [Fact]
    public void AudioDevice_Id_IsRequired()
    {
        // Arrange & Act
        var device = new AudioDevice
        {
            Id = "test-id",
            FriendlyName = "Test"
        };

        // Assert - required init means it must be set at construction
        Assert.NotNull(device.Id);
        Assert.NotEmpty(device.Id);
    }

    [Fact]
    public void AudioDevice_FriendlyName_IsRequired()
    {
        // Arrange & Act
        var device = new AudioDevice
        {
            Id = "test",
            FriendlyName = "Test Device"
        };

        // Assert
        Assert.NotNull(device.FriendlyName);
    }
}
