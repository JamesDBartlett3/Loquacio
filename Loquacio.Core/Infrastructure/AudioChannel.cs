using System.Threading.Channels;

namespace Loquacio.Infrastructure;

public static class AudioChannel
{
    private static readonly Channel<AudioSegment> _channel =
        Channel.CreateUnbounded<AudioSegment>();

    public static ChannelReader<AudioSegment> Reader => _channel.Reader;
    public static ChannelWriter<AudioSegment> Writer => _channel.Writer;

    public static ValueTask WriteAsync(AudioSegment segment, CancellationToken ct = default) =>
        _channel.Writer.WriteAsync(segment, ct);

    public static ValueTask<AudioSegment> ReadAsync(CancellationToken ct = default) =>
        _channel.Reader.ReadAsync(ct);

    /// <summary>
    /// Read all segments as they become available (for consumer loops)
    /// </summary>
    public static IAsyncEnumerable<AudioSegment> ReadAllAsync(CancellationToken ct = default) =>
        _channel.Reader.ReadAllAsync(ct);
}