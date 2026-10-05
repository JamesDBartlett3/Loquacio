using WhisperDictation.Services;

namespace WhisperDictation.Tests.Services;

public class AudioCompressorTests
{
    [Fact]
    public void Process16BitPcm_CompressesSamplesAboveThreshold()
    {
        var buffer = new byte[4];
        BitConverter.TryWriteBytes(buffer.AsSpan(0, 2), (short)32767);
        BitConverter.TryWriteBytes(buffer.AsSpan(2, 2), (short)3000);

        AudioCompressor.Process16BitPcm(buffer, thresholdDb: -18, ratio: 4);

        var peak = BitConverter.ToInt16(buffer, 0);
        var belowThreshold = BitConverter.ToInt16(buffer, 2);

        Assert.InRange(peak, 11000, 11600);
        Assert.Equal(3000, belowThreshold);
    }

    [Fact]
    public void Process16BitPcm_PreservesSign()
    {
        var buffer = new byte[2];
        BitConverter.TryWriteBytes(buffer, (short)-32768);

        AudioCompressor.Process16BitPcm(buffer, thresholdDb: -18, ratio: 4);

        Assert.True(BitConverter.ToInt16(buffer) < 0);
    }
}
