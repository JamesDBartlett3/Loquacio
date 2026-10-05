using WhisperDictation.Models;

namespace WhisperDictation.Tests.Models;

public class TranscriptionResultTests
{
    [Fact]
    public void TranscriptionResult_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var result = new TranscriptionResult();

        // Assert
        Assert.Equal(string.Empty, result.Text);
        Assert.Equal(default(DateTime), result.Timestamp);
        Assert.Equal(TimeSpan.Zero, result.Duration);
        Assert.Null(result.Language);
        Assert.False(result.IsPostProcessed);
        Assert.Null(result.RawText);
    }

    [Fact]
    public void TranscriptionResult_WithValues_PropertiesSetCorrectly()
    {
        // Arrange
        var timestamp = DateTime.UtcNow;

        // Act
        var result = new TranscriptionResult
        {
            Text = "Hello, world!",
            Timestamp = timestamp,
            Duration = TimeSpan.FromSeconds(3.5),
            Language = "en",
            IsPostProcessed = true,
            RawText = "hello world"
        };

        // Assert
        Assert.Equal("Hello, world!", result.Text);
        Assert.Equal(timestamp, result.Timestamp);
        Assert.Equal(TimeSpan.FromSeconds(3.5), result.Duration);
        Assert.Equal("en", result.Language);
        Assert.True(result.IsPostProcessed);
        Assert.Equal("hello world", result.RawText);
    }

    [Fact]
    public void TranscriptionResult_CanRepresentRawAndProcessed()
    {
        // Arrange & Act
        var result = new TranscriptionResult
        {
            Text = "Hello, world! This is corrected.",
            RawText = "hello world this is corrected",
            IsPostProcessed = true
        };

        // Assert
        Assert.NotEqual(result.Text, result.RawText);
        Assert.True(result.IsPostProcessed);
    }
}
