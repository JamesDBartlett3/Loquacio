using System.Reflection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using WhisperDictation.Daemon.Interop;
using WhisperDictation.Daemon.Ipc;
using WhisperDictation.Daemon.Services;

namespace WhisperDictation.Daemon;

/// <summary>
/// Entry point for the Whisper Dictation daemon.
/// Runs as a headless background process that owns the audio pipeline.
/// Controllers (TUI, GUI) connect via IPC.
/// </summary>
public static class Program
{
    public static async Task Main(string[] args)
    {
        if (HandleInfoArgs(args))
            return;

        // Single-instance enforcement via PID file
        var pidPath = GetPidFilePath();
        if (TryAcquirePidLock(pidPath, out var pidFile))
        {
            try
            {
                await RunHost(args);
            }
            finally
            {
                TryReleasePidLock(pidPath, pidFile);
            }
        }
        else
        {
            Console.Error.WriteLine("Whisper Dictation daemon is already running.");
            Environment.Exit(1);
        }
    }

    // ── --help / --version ──

    private static bool HandleInfoArgs(string[] args)
    {
        if (args.Length == 0) return false;
        switch (args[0].ToLowerInvariant())
        {
            case "-h":
            case "--help":
            case "help":
                Console.WriteLine("""
                Whisper Dictation daemon

                Runs as a headless background process that owns the audio pipeline.
                Controllers (TUI, GUI) connect via IPC (Unix socket / named pipe).

                Usage: whisper-dictation-daemon [options]

                Options:
                  -h, --help     Show this help and exit
                  -v, --version  Show version information and exit

                Configuration is read from appsettings.json, WHISPER_* environment
                variables, and command-line arguments (Microsoft.Extensions.Configuration).
                Usually started via the systemd user unit:
                  systemctl --user start whisper-dictation-daemon
                """);
                return true;

            case "-v":
            case "--version":
            case "version":
                Console.WriteLine($"whisper-dictation-daemon {GetVersion()}");
                return true;

            default:
                return false;
        }
    }

    private static string GetVersion()
    {
        var attr = typeof(Program).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        return attr?.InformationalVersion ?? typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown";
    }

    private static async Task RunHost(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        // Configuration
        builder.Configuration.SetBasePath(AppContext.BaseDirectory);
        builder.Configuration.AddJsonFile("appsettings.json", optional: true);
        builder.Configuration.AddEnvironmentVariables("WHISPER_");
        builder.Configuration.AddCommandLine(args);

        // Logging
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();

        // --- All daemon services (single source of truth; shared with InProcessDaemonHost) ---
        builder.Services.AddWhisperDaemonServices();

        // --- Hosted lifecycle (external daemon process only) ---
        builder.Services.AddHostedService<DaemonHostedService>();

        // SIGTERM / Ctrl+C → graceful shutdown
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        builder.Services.Configure<HostOptions>(o =>
        {
            o.ShutdownTimeout = TimeSpan.FromSeconds(5);
        });

        var host = builder.Build();

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            cts.Cancel();
        };

        await host.RunAsync(cts.Token);
    }

    // ── PID file management for single-instance enforcement ──

    private static string GetPidFilePath()
    {
        // Linux: ~/.local/share/whisper-dictation/daemon.pid
        // Windows: %LOCALAPPDATA%\WhisperDictation\daemon.pid
        var dir = Environment.GetEnvironmentVariable("XDG_DATA_HOME")
                  ?? Environment.GetEnvironmentVariable("LOCALAPPDATA")
                  ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");

        var appDir = Path.Combine(dir, "whisper-dictation");
        Directory.CreateDirectory(appDir);
        return Path.Combine(appDir, "daemon.pid");
    }

    private static bool TryAcquirePidLock(string path, out FileStream? stream)
    {
        // FileShare.Read: the lock still enforces single-instance (CreateNew
        // fails when the file exists), but controllers can read the PID to
        // stop the daemon when the UI exits.
        try
        {
            stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(stream, leaveOpen: true);
            writer.WriteLine(Environment.ProcessId);
            writer.Flush();
            return true;
        }
        catch (IOException)
        {
            // File exists — check if the PID is still alive
            try
            {
                var existingPid = int.Parse(File.ReadAllText(path).Trim());
                if (IsProcessAlive(existingPid))
                {
                    stream = null;
                    return false;
                }
                // Stale PID file — remove and retry
                File.Delete(path);
                stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                using var writer = new StreamWriter(stream, leaveOpen: true);
                writer.WriteLine(Environment.ProcessId);
                writer.Flush();
                return true;
            }
            catch
            {
                stream = null;
                return false;
            }
        }
    }

    private static void TryReleasePidLock(string path, FileStream? stream)
    {
        stream?.Dispose();
        try { File.Delete(path); } catch { /* best effort */ }
    }

    private static bool IsProcessAlive(int pid)
    {
        try
        {
            var proc = System.Diagnostics.Process.GetProcessById(pid);
            return !proc.HasExited;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Permission denied — assume alive (conservative)
            return true;
        }
        catch (System.ArgumentException)
        {
            return false;
        }
    }
}
