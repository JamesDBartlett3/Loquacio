using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Loquacio.Core.Services;
using Loquacio.Daemon.Interop;
using Loquacio.Daemon.Ipc;
using Loquacio.Daemon.Services;

namespace Loquacio.Daemon;

/// <summary>
/// Registers the complete daemon service graph (audio pipeline + IPC + hotkeys).
///
/// This is the SINGLE source of truth for daemon DI. Both the standalone daemon
/// host (<c>Program.cs</c>) and the in-process daemon option
/// (<see cref="Services.InProcessDaemonHost"/>, used by the WPF controller) use
/// this extension so the two contexts can never drift apart.
/// </summary>
public static class DaemonServiceRegistration
{
    /// <summary>
    /// Register all daemon services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="socketPathOverride">Optional override for the IPC server socket/pipe path (used by tests).</param>
    /// <param name="configuration">Optional pre-built configuration. When null, one is built from appsettings.json next to the entry assembly.</param>
    public static IServiceCollection AddWhisperDaemonServices(
        this IServiceCollection services,
        string? socketPathOverride = null,
        IConfiguration? configuration = null)
    {
        // Configuration (HotkeyManager + SettingsService read from it when hosted
        // outside a Generic Host — e.g. in-process inside the WPF app or tests).
        if (configuration != null)
        {
            services.TryAddSingleton(configuration);
        }
        else
        {
            services.TryAddSingleton<IConfiguration>(_ =>
            {
                var builder = new ConfigurationBuilder();
                builder.SetBasePath(AppContext.BaseDirectory);
                builder.AddJsonFile("appsettings.json", optional: true);
                builder.AddEnvironmentVariables("WHISPER_");
                return builder.Build();
            });
        }

        // --- Core pipeline services (cross-platform, from Loquacio.Core) ---
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IHistoryService, HistoryService>();
        services.AddSingleton<IVocabularyService, VocabularyService>();
        services.AddSingleton<IModelManagerService, ModelManagerService>();
        services.AddSingleton<IWhisperProcessorService, WhisperProcessorService>();
        services.AddSingleton<ILLMPostProcessorService, LLMPostProcessorService>();
        services.AddSingleton<IBackgroundTranscriptionService, BackgroundTranscriptionService>();
        services.AddSingleton<IActivationManagerService, ActivationManagerService>();

        // Auto-update service (GitHub-based). Uses a named HttpClient (UA header)
        // constructed via factory so the ctor's config params are supplied here
        // (MS DI cannot inject default-valued params, and the typed-client
        // pattern cannot supply IHostApplicationLifetime/repository strings).
        services.AddHttpClient("GitHubUpdates", client =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Loquacio/1.0 (+https://github.com/JamesDBartlett3/loquacio)");
        });
        services.AddSingleton<IUpdateService>(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient("GitHubUpdates");
            var logger = sp.GetRequiredService<ILogger<GitHubUpdateService>>();
            var hostApplicationLifetime = sp.GetService<IHostApplicationLifetime>();
            var configuration = sp.GetRequiredService<IConfiguration>();

            var repositoryOwner = configuration["Update:RepositoryOwner"] ?? "JamesDBartlett3";
            var repositoryName = configuration["Update:RepositoryName"] ?? "loquacio";
            var currentVersion = configuration["Update:CurrentVersion"];

            return new GitHubUpdateService(httpClient, logger, hostApplicationLifetime, repositoryOwner, repositoryName, currentVersion);
        });

        // Keyword detection (energy-based VAD over the shared audio capture service)
        services.AddSingleton<IKeywordDetectionService, KeywordDetectionService>();

        // --- Platform-specific implementations ---
        // Audio capture: WASAPI on Windows; PipeWire (pw-record) on Linux;
        // explicit "unsupported" fallback elsewhere (e.g. macOS, Phase D item 1).
        if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<IAudioCaptureService, Services.Audio.WasapiAudioCaptureService>();
        }
        else if (OperatingSystem.IsLinux())
        {
            services.AddSingleton<IAudioCaptureService, Services.Audio.PipeWireAudioCaptureService>();
        }
        else
        {
            services.AddSingleton<IAudioCaptureService, UnsupportedAudioCaptureService>();
        }

        // Global hotkeys in the daemon are owned by HotkeyManager (X11/Win32).
        // NullHotkeyService satisfies the legacy IHotkeyService dependency of
        // ActivationManagerService without competing for system hotkey registration.
        services.AddSingleton<IHotkeyService, NullHotkeyService>();

        // X11 interop (Linux only; on Windows the HotkeyManager uses Win32 directly)
        if (OperatingSystem.IsLinux())
        {
            services.AddSingleton<IX11Interop, X11Interop>();
        }

        // --- Daemon-specific services ---
        services.AddSingleton<ITextInjectionService, TextInjectionService>();
        services.AddSingleton<IClipboardService, DaemonClipboardService>();
        services.AddSingleton<HotkeyManager>();
        services.AddSingleton<DaemonPipeline>();

        services.AddSingleton<IpcServer>(sp => socketPathOverride is null
            ? ActivatorUtilities.CreateInstance<IpcServer>(sp)
            : ActivatorUtilities.CreateInstance<IpcServer>(sp, socketPathOverride));

        return services;
    }
}
