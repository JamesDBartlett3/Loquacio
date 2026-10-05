using Loquacio.Models;

namespace Loquacio.Tests.Models;

public class AudioSegmentTests
{
    [Fact]
    public void AudioSegment_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var segment = new AudioSegment();

        // Assert
        Assert.Null(segment.Data);
        Assert.Equal(0, segment.SampleRate);
        Assert.Equal(0, segment.Channels);
        Assert.Equal(0, segment.BitsPerSample);
        Assert.Equal(TimeSpan.Zero, segment.Duration);
        Assert.True(segment.Timestamp <= DateTime.UtcNow);
        Assert.True(segment.Timestamp > DateTime.UtcNow.AddSeconds(-5));
    }

    [Fact]
    public void AudioSegment_WithValues_PropertiesSetCorrectly()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3, 4, 5, 6 };
        var timestamp = DateTime.UtcNow;

        // Act
        var segment = new AudioSegment
        {
            Data = data,
            SampleRate = 48000,
            Channels = 1,
            BitsPerSample = 24,
            Duration = TimeSpan.FromSeconds(2.5),
            Timestamp = timestamp
        };

        // Assert
        Assert.Equal(data, segment.Data);
        Assert.Equal(48000, segment.SampleRate);
        Assert.Equal(1, segment.Channels);
        Assert.Equal(24, segment.BitsPerSample);
        Assert.Equal(TimeSpan.FromSeconds(2.5), segment.Duration);
        Assert.Equal(timestamp, segment.Timestamp);
    }

    [Fact]
    public void AudioSegment_DataCanBeModified()
    {
        // Arrange
        var segment = new AudioSegment { Data = new byte[] { 1, 2, 3 } };

        // Act
        segment.Data![0] = 99;

        // Assert
        Assert.Equal(99, segment.Data[0]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(48000)]
    [InlineData(44100)]
    public void AudioSegment_SampleRate_AcceptsValidValues(int sampleRate)
    {
        // Arrange & Act
        var segment = new AudioSegment { SampleRate = sampleRate };

        // Assert
        Assert.Equal(sampleRate, segment.SampleRate);
    }
}
