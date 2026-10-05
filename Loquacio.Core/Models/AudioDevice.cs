namespace Loquacio.Models;

/// <summary>
/// Represents an audio capture device
/// </summary>
public class AudioDevice
{
    /// <summary>
    /// Unique device identifier (WASAPI device ID)
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Human-readable device name
    /// </summary>
    public required string FriendlyName { get; init; }

    /// <summary>
    /// Whether this device is a capture (input) device
    /// </summary>
    public bool IsCaptureDevice { get; init; }
}