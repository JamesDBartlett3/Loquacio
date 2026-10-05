using Microsoft.Extensions.Logging.Abstractions;
using WhisperDictation.Models;
using WhisperDictation.Services;

namespace WhisperDictation.Tests.Services;

/// <summary>
/// End-to-end integration test: real Whisper model + real speech audio through the
/// actual WhisperProcessorService pipeline (the same code path the daemon uses).
///
/// Gated behind environment variables so CI/fast test runs skip it:
///   WHISPER_E2E_MODEL_PATH — path to a ggml Whisper model (e.g. ggml-tiny.bin)
///   WHISPER_E2E_AUDIO_PATH — path to a 16-bit PCM wav (16kHz mono) speech sample
///
/// Run example:
///   WHISPER_E2E_MODEL_PATH=/tmp/whisper-e2e/ggml-tiny.bin \
///   WHISPER_E2E_AUDIO_PATH=/tmp/whisper-e2e/jfk.wav \
///   dotnet test --filter FullyQualifiedName~EndToEnd
/// </summary>
public class EndToEndDictationTests
{
    private static readonly string? ModelPath = Environment.GetEnvironmentVariable("WHISPER_E2E_MODEL_PATH");
    private static readonly string? AudioPath = Environment.GetEnvironmentVariable("WHISPER_E2E_AUDIO_PATH");

    public static bool Enabled =>
        !string.IsNullOrWhiteSpace(ModelPath) && File.Exists(ModelPath) &&
        !string.IsNullOrWhiteSpace(AudioPath) && File.Exists(AudioPath);

    [EndToEndFact]
    public void RealWhisperModel_TranscribesKnownSpeechSample()
    {

        // Arrange: parse wav (expect 16-bit PCM; header-agnostic for the common canonical layout)
        var wav = File.ReadAllBytes(AudioPath!);
        int sampleRate = BitConverter.ToInt32(wav, 24);
        int bitsPerSample = BitConverter.ToInt16(wav, 34);
        int dataOffset = FindDataChunk(wav);
        var pcm = wav[dataOffset..];

        var service = new WhisperProcessorService(NullLogger<WhisperProcessorService>.Instance);
        try
        {
            service.LoadModel(ModelPath!, "en");
            Assert.True(service.IsModelLoaded);

            var segment = new AudioSegment
            {
                Data = pcm,
                SampleRate = sampleRate,
                Channels = 1,
                BitsPerSample = bitsPerSample
            };

            // Act
            var transcript = service.ProcessAsync(segment).GetAwaiter().GetResult();

            // Assert: tiny model on the classic JFK sample reliably contains these words.
            Assert.False(string.IsNullOrWhiteSpace(transcript));
            var lower = transcript.ToLowerInvariant();
            Assert.True(
                lower.Contains("fellow") || lower.Contains("americans") || lower.Contains("ask"),
                $"Transcript did not contain expected words. Got: '{transcript}'");
        }
        finally
        {
            service.Dispose();
        }
    }

    private static int FindDataChunk(byte[] wav)
    {
        int offset = 12;
        while (offset + 8 <= wav.Length)
        {
            string id = System.Text.Encoding.ASCII.GetString(wav, offset, 4);
            int size = BitConverter.ToInt32(wav, offset + 4);
            if (id == "data") return offset + 8;
            offset += 8 + size + (size % 2); // chunks are word-aligned
        }
        throw new InvalidDataException("No data chunk found in wav file");
    }
}

/// <summary>Fact that runs only when WHISPER_E2E_MODEL_PATH/WHISPER_E2E_AUDIO_PATH are set and valid.</summary>
public sealed class EndToEndFactAttribute : FactAttribute
{
    public EndToEndFactAttribute()
    {
        if (!EndToEndDictationTests.Enabled)
            Skip = "Set WHISPER_E2E_MODEL_PATH and WHISPER_E2E_AUDIO_PATH to enable";
    }
}
