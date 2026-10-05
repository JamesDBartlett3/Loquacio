using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WhisperDictation.Models;
using WhisperDictation.Services;

namespace CrossPlatformTestConsole;

/// <summary>
/// Minimal cross-platform smoke-test console. Exercises the platform-neutral
/// core services (settings + model manager) to verify they load and respond
/// on any OS supported by WhisperDictation.Core.
/// </summary>
internal sealed class Program
{
    private static async Task<int> Main(string[] args)
    {
        var services = new ServiceCollection();

        services.AddLogging(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Information));

        // Cross-platform core services only (no Windows-specific ones).
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IModelManagerService, ModelManagerService>();

        await using var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

        logger.LogInformation("Starting Cross-Platform Whisper Dictation Test Console");

        try
        {
            // Test model manager: enumerate the built-in model catalog.
            var modelManager = serviceProvider.GetRequiredService<IModelManagerService>();
            logger.LogInformation("Models directory: {ModelsDirectory}", modelManager.ModelsDirectory);
            var catalog = modelManager.AvailableModels;
            logger.LogInformation("Available models in catalog: {Count}", catalog.Count);
            foreach (var model in catalog)
            {
                logger.LogInformation(
                    "  - {Name} ({DisplayName}): {SizeBytes} bytes, downloaded: {Downloaded}",
                    model.Name, model.DisplayName, model.SizeBytes, modelManager.IsModelDownloaded(model));
            }

            var downloaded = await modelManager.GetDownloadedModelsAsync();
            logger.LogInformation("Downloaded models: {Count}", downloaded.Count);

            // Test settings service: load and report a couple of key values.
            var settingsService = serviceProvider.GetRequiredService<ISettingsService>();
            var settings = await settingsService.GetSettingsAsync();
            logger.LogInformation("Settings loaded successfully");
            logger.LogInformation("Whisper model: {Model}", settings.Whisper.ModelPath);
            logger.LogInformation("LLM enabled: {LlmEnabled}", settings.LLM.Enabled);

            // Verify settings round-trip through the snapshot helper.
            var snapshot = settings.Snapshot();
            if (snapshot.Whisper.ModelPath != settings.Whisper.ModelPath)
            {
                throw new InvalidOperationException("Settings snapshot did not round-trip");
            }
            logger.LogInformation("Settings snapshot round-trip verified");

            logger.LogInformation("Cross-platform tests completed successfully!");
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during cross-platform testing");
            return 1;
        }
    }
}
