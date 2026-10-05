namespace Loquacio.Services;

/// <summary>
/// Simple sample-domain downward compressor for normalized PCM audio.
/// </summary>
public static class AudioCompressor
{
    public static void Process16BitPcm(Span<byte> buffer, double thresholdDb, double ratio)
    {
        var threshold = Math.Pow(10, Math.Clamp(thresholdDb, -60, 0) / 20);
        var safeRatio = Math.Max(1, ratio);

        for (var offset = 0; offset + 1 < buffer.Length; offset += 2)
        {
            var sample = BitConverter.ToInt16(buffer[offset..(offset + 2)]);
            var sign = Math.Sign(sample);
            var magnitude = Math.Abs((double)sample) / short.MaxValue;

            if (magnitude > threshold)
            {
                var compressedMagnitude = threshold +
                    (magnitude - threshold) / safeRatio;
                magnitude = Math.Min(1, compressedMagnitude);
            }

            var compressed = (short)Math.Clamp(
                sign * magnitude * short.MaxValue, short.MinValue, short.MaxValue);
            BitConverter.TryWriteBytes(buffer[offset..(offset + 2)], compressed);
        }
    }
}
