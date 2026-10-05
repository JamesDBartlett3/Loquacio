using WhisperDictation.Infrastructure;
using WhisperDictation.Models;

namespace WhisperDictation.Tests.Infrastructure;

public class AudioChannelTests : IDisposable
{
    [Fact]
    public async Task WriteAsync_SegmentIsReadable()
    {
        // Arrange
        var segment = new AudioSegment
        {
            Data = new byte[] { 1, 2, 3 },
            SampleRate = 48000,
            Channels = 1,
            BitsPerSample = 24
        };

        // Act
        await AudioChannel.WriteAsync(segment);
        var read = await AudioChannel.ReadAsync();

        // Assert
        Assert.Same(segment, read);
    }

    [Fact]
    public async Task WriteAsync_MultipleSegments_ReadInOrder()
    {
        // Arrange
        var seg1 = new AudioSegment { Data = new byte[] { 1 } };
        var seg2 = new AudioSegment { Data = new byte[] { 2 } };
        var seg3 = new AudioSegment { Data = new byte[] { 3 } };

        // Act
        await AudioChannel.WriteAsync(seg1);
        await AudioChannel.WriteAsync(seg2);
        await AudioChannel.WriteAsync(seg3);

        var read1 = await AudioChannel.ReadAsync();
        var read2 = await AudioChannel.ReadAsync();
        var read3 = await AudioChannel.ReadAsync();

        // Assert - FIFO order
        Assert.Same(seg1, read1);
        Assert.Same(seg2, read2);
        Assert.Same(seg3, read3);
    }

    [Fact]
    public async Task WriteAsync_WithCancellationToken_RespectsCancellation()
    {
        // Arrange
        var segment = new AudioSegment { Data = new byte[] { 1 } };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        // Writing to unbounded channel should succeed even with cancelled token
        // but reading from empty channel should throw OperationCanceledException
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await AudioChannel.ReadAsync(cts.Token);
        });
    }

    [Fact]
    public async Task ReadAllAsync_ReturnsAllWrittenSegments()
    {
        // Arrange
        var segments = new[]
        {
            new AudioSegment { Data = new byte[] { 1 } },
            new AudioSegment { Data = new byte[] { 2 } },
            new AudioSegment { Data = new byte[] { 3 } }
        };

        using var cts = new CancellationTokenSource();

        // Act
        foreach (var seg in segments)
        {
            await AudioChannel.WriteAsync(seg);
        }

        // Read all segments using ReadAllAsync
        var readSegments = new List<AudioSegment>();
        await foreach (var seg in AudioChannel.ReadAllAsync(cts.Token))
        {
            readSegments.Add(seg);
            if (readSegments.Count >= 3)
                break;
        }

        // Assert
        Assert.Equal(3, readSegments.Count);
        Assert.Same(segments[0], readSegments[0]);
        Assert.Same(segments[1], readSegments[1]);
        Assert.Same(segments[2], readSegments[2]);
    }

    public void Dispose()
    {
        // Drain any remaining items to prevent cross-test contamination
        // AudioChannel is static, so we need to clear it between tests
        while (AudioChannel.Reader.TryRead(out _)) { }
    }
}
