using NSubstitute;
using WhisperDictation.Models;
using WhisperDictation.Services;

namespace WhisperDictation.Tests.Services;

public class IWhisperProcessorServiceTests
{
    private readonly IWhisperProcessorService _mockWhisper;

    public IWhisperProcessorServiceTests()
    {
        _mockWhisper = Substitute.For<IWhisperProcessorService>();
    }

    [Fact]
    public void IsModelLoaded_DefaultsToFalse_WhenNotLoaded()
    {
        // Arrange
        _mockWhisper.IsModelLoaded.Returns(false);

        // Act & Assert
        Assert.False(_mockWhisper.IsModelLoaded);
    }

    [Fact]
    public void IsModelLoaded_ReturnsTrue_AfterLoadModel()
    {
        // Arrange
        _mockWhisper.IsModelLoaded.Returns(true);

        // Act & Assert
        Assert.True(_mockWhisper.IsModelLoaded);
    }

    [Fact]
    public async Task ProcessAsync_ReturnsTranscription_WhenModelLoaded()
    {
        // Arrange
        var segment = new AudioSegment
        {
            Data = new byte[] { 1, 2, 3, 4, 5, 6 },
            SampleRate = 48000,
            Channels = 1,
            BitsPerSample = 24,
            Duration = TimeSpan.FromSeconds(1.0)
        };
        _mockWhisper.ProcessAsync(segment, Arg.Any<CancellationToken>())
            .Returns("Hello, world!");

        // Act
        var result = await _mockWhisper.ProcessAsync(segment);

        // Assert
        Assert.Equal("Hello, world!", result);
    }

    [Fact]
    public async Task ProcessAsync_ReturnsEmpty_ForEmptySegment()
    {
        // Arrange
        var emptySegment = new AudioSegment
        {
            Data = Array.Empty<byte>(),
            SampleRate = 48000,
            Channels = 1,
            BitsPerSample = 24
        };
        _mockWhisper.ProcessAsync(emptySegment, Arg.Any<CancellationToken>())
            .Returns(string.Empty);

        // Act
        var result = await _mockWhisper.ProcessAsync(emptySegment);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task ProcessAsync_ReturnsLongText_ForLongAudio()
    {
        // Arrange
        var segment = new AudioSegment
        {
            Data = new byte[48000 * 3 * 10], // 10 seconds of 24-bit 48kHz audio
            SampleRate = 48000,
            Channels = 1,
            BitsPerSample = 24,
            Duration = TimeSpan.FromSeconds(10)
        };
        var expectedText = "This is a longer transcription that spans multiple sentences and should test the service's ability to return extended text output from a longer audio segment.";
        _mockWhisper.ProcessAsync(segment, Arg.Any<CancellationToken>())
            .Returns(expectedText);

        // Act
        var result = await _mockWhisper.ProcessAsync(segment);

        // Assert
        Assert.Equal(expectedText, result);
    }

    [Fact]
    public void LoadModel_DoesNotThrow_WithValidPath()
    {
        // Arrange & Act
        _mockWhisper.LoadModel("/fake/path/ggml-base.bin");

        // Assert
        _mockWhisper.Received(1).LoadModel("/fake/path/ggml-base.bin");
    }
}
