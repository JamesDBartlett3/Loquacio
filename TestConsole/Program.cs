using Loquacio.Models;
using Loquacio.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

// Setup dependency injection
var services = new ServiceCollection();

// Add logging
services.AddLogging(configure =>
{
    configure.AddConsole();
    configure.SetMinimumLevel(LogLevel.Debug);
});

// Add services
services.AddSingleton<IAudioCaptureService, Loquacio.Engine.Services.Audio.WasapiAudioCaptureService>();
services.AddSingleton<IWhisperProcessorService, WhisperProcessorService>();
services.AddSingleton<IModelManagerService, ModelManagerService>();
services.AddSingleton<ISettingsService, SettingsService>();
services.AddSingleton<IBackgroundTranscriptionService, BackgroundTranscriptionService>();

var serviceProvider = services.BuildServiceProvider();

var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
var audioCapture = serviceProvider.GetRequiredService<IAudioCaptureService>();
var whisperProcessor = serviceProvider.GetRequiredService<IWhisperProcessorService>();
var modelManager = serviceProvider.GetRequiredService<IModelManagerService>();
var bgTranscription = serviceProvider.GetRequiredService<IBackgroundTranscriptionService>();

Console.WriteLine("=== Loquacio - Console Test App ===");
Console.WriteLine();

// Step 1: List available models
Console.WriteLine("Available Whisper models:");
foreach (var model in modelManager.AvailableModels)
{
    Console.WriteLine($"  - {model.DisplayName}");
    Console.WriteLine($"    Size: {model.SizeBytes / 1024 / 1024:N0} MB");
    Console.WriteLine($"    Use: {model.UseCase}");
    Console.WriteLine();
}

// Step 2: Check downloaded models
Console.WriteLine("Checking downloaded models...");
var downloadedModels = await modelManager.GetDownloadedModelsAsync();
if (downloadedModels.Count == 0)
{
    Console.WriteLine("No models downloaded.");
    Console.WriteLine();
    Console.WriteLine("To download a model, run:");
    Console.WriteLine("  WhisperGgmlDownloader -m tiny -o \"%AppData%\\Loquacio\\models\\ggml-tiny.bin\"");
    Console.WriteLine();
    return;
}

Console.WriteLine($"Found {downloadedModels.Count} downloaded model(s):");
foreach (var model in downloadedModels)
{
    var path = modelManager.GetModelPath(model);
    var fileInfo = new FileInfo(path);
    Console.WriteLine($"  - {model.DisplayName} ({fileInfo.Length / 1024 / 1024:N0} MB)");
    Console.WriteLine($"    Path: {path}");
}
Console.WriteLine();

// Step 3: Select model to use
var selectedModel = downloadedModels[0];
Console.WriteLine($"Using model: {selectedModel.DisplayName}");

// Step 4: Load the model
Console.WriteLine();
Console.WriteLine("Loading Whisper model...");
try
{
    whisperProcessor.LoadModel(modelManager.GetModelPath(selectedModel));
    Console.WriteLine("Model loaded successfully!");
}
catch (Exception ex)
{
    logger.LogError(ex, "Failed to load Whisper model");
    return;
}

// Step 5: List audio devices
Console.WriteLine();
Console.WriteLine("Available audio devices:");
var devices = audioCapture.GetAvailableDevices();
if (devices.Count == 0)
{
    Console.WriteLine("No audio capture devices available!");
    return;
}

for (int i = 0; i < devices.Count; i++)
{
    Console.WriteLine($"  [{i}] {devices[i].FriendlyName}");
}
Console.WriteLine();

// Step 6: Select audio device
var selectedDevice = devices[0];
Console.WriteLine($"Using audio device: {selectedDevice.FriendlyName}");

// Step 7: Subscribe to transcription events
Console.WriteLine();
Console.WriteLine("Starting transcription test...");
Console.WriteLine("Press Ctrl+C to stop.");
Console.WriteLine();

bool isTranscribing = true;
bgTranscription.OnTranscriptionCompleted += (sender, result) =>
{
    Console.WriteLine($"[{result.Timestamp:HH:mm:ss}] {result.Text}");
};

bgTranscription.OnError += (sender, ex) =>
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"Error: {ex.Message}");
    Console.ResetColor();
};

Console.CancelKeyPress += (sender, e) =>
{
    e.Cancel = true;
    isTranscribing = false;
    Console.WriteLine();
    Console.WriteLine("Stopping transcription...");
};

// Step 8: Start transcription
try
{
    await bgTranscription.StartAsync();

    // Keep running until Ctrl+C
    while (isTranscribing)
    {
        await Task.Delay(100);
    }
}
catch (Exception ex)
{
    logger.LogError(ex, "Error during transcription");
}
finally
{
    await bgTranscription.StopAsync();
    Console.WriteLine("Transcription stopped.");
}

Console.WriteLine();
Console.WriteLine("Test complete.");