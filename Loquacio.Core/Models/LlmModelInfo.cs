namespace Loquacio.Models;

/// <summary>Metadata reported by a local LLM provider for model selection.</summary>
public sealed class LlmModelInfo
{
    public string Id { get; init; } = string.Empty;
    public string Architecture { get; init; } = "Unknown";
    public string Parameters { get; init; } = "Unknown";
    public string Publisher { get; init; } = "Unknown";
    public string Quantization { get; init; } = "Unknown";
    public string MemorySize { get; init; } = "Unknown";

    /// <summary>Multi-line summary for tooltips (combo-box items show only the name).</summary>
    public string Summary =>
        $"Architecture: {Architecture}\nParameters: {Parameters}\nPublisher: {Publisher}\nQuantization: {Quantization}\nMemory: {MemorySize}";
}
