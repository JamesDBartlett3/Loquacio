using Loquacio.Models;

namespace Loquacio.Services;

public interface IWhisperProcessorService
{
    Task<string> ProcessAsync(AudioSegment segment, CancellationToken ct = default);
    void LoadModel(string modelPath, string language = "auto");
    bool IsModelLoaded { get; }
}