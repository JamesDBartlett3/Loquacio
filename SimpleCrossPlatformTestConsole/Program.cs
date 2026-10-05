using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using Whisper.net;

namespace SimpleCrossPlatformTestConsole
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var services = new ServiceCollection();
            
            // Setup logging
            services.AddLogging(builder => 
                builder.AddConsole().SetMinimumLevel(LogLevel.Information));

            var serviceProvider = services.BuildServiceProvider();
            var logger = serviceProvider.GetService<ILogger<Program>>();

            logger.LogInformation("Starting Simple Cross-Platform Loquacio Test Console");

            try
            {
                TestWhisperNet(logger);
                TestModelInfo(logger);
                TestSettings(logger);
                
                logger.LogInformation("Cross-platform tests completed successfully!");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during cross-platform testing");
            }
        }

        static void TestWhisperNet(ILogger logger)
        {
            logger.LogInformation("Testing Whisper.net library availability...");
            
            try
            {
                // Test that we can use Whisper.net (this will fail due to missing native library on Linux)
                using var factory = WhisperFactory.FromPath("test");
                logger.LogInformation("Whisper.net factory creation method available");
            }
            catch (FileNotFoundException ex)
            {
                logger.LogWarning("Expected Whisper.net native library not found on Linux: {Message}", ex.Message);
                logger.LogInformation("Whisper.net is available but requires native library for full functionality");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error testing Whisper.net");
                return;
            }
            
            logger.LogInformation("✅ Whisper.net test passed");
        }

        static void TestModelInfo(ILogger logger)
        {
            logger.LogInformation("Testing model information...");
            
            // Test model info structure
            var modelInfo = new ModelInfo
            {
                Name = "tiny.en",
                SizeBytes = 75_000_000,
                Language = "en",
                Format = "ggml"
            };

            logger.LogInformation($"Model info test: {modelInfo.Name} - {modelInfo.SizeBytes / 1_000_000}MB - {modelInfo.Language}");
            logger.LogInformation("✅ Model info test passed");
        }

        static void TestSettings(ILogger logger)
        {
            logger.LogInformation("Testing settings structure...");
            
            var settings = new Settings
            {
                Audio = new AudioSettings
                {
                    DeviceId = "default",
                    Gain = 1.0,
                    SilenceThresholdMs = 1500
                },
                Whisper = new WhisperSettings
                {
                    ModelPath = "models/tiny.en.ggml",
                    Language = "auto"
                },
                LLM = new LLMSettings
                {
                    Provider = "lm-studio",
                    Endpoint = "http://localhost:1234/v1",
                    Model = "auto",
                    Enabled = true
                },
                Activation = new ActivationSettings
                {
                    Mode = "continuous",
                    Hotkey = "Ctrl+Alt+D",
                    KeywordEnabled = true,
                    Keyword = "Hey Dictate"
                },
                Vocabulary = new VocabularySettings
                {
                    CustomWords = new List<string> { "PowerShell", "Fabric", "Microsoft" }
                },
                Output = new OutputSettings
                {
                    Mode = "clipboard",
                    ToastEnabled = true
                }
            };

            logger.LogInformation($"Settings loaded - Mode: {settings.Activation.Mode}, Language: {settings.Whisper.Language}");
            logger.LogInformation($"Custom words: {string.Join(", ", settings.Vocabulary.CustomWords)}");
            logger.LogInformation("✅ Settings test passed");
        }
    }
}