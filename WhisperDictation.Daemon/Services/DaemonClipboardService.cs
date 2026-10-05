using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace WhisperDictation.Daemon.Services;

/// <summary>
/// Cross-platform <see cref="IClipboardService"/> for the daemon.
///
/// Copy: xclip / wl-copy on Linux, PowerShell Set-Clipboard on Windows.
/// Type: delegates to <see cref="ITextInjectionService"/>, falling back to
/// clipboard copy when injection is blocked (elevated window, UIPI, etc.).
/// </summary>
public class DaemonClipboardService(
    ITextInjectionService textInjection,
    ILogger<DaemonClipboardService> logger) : IClipboardService
{
    private string _outputMode = "clipboard";

    public string OutputMode
    {
        get => _outputMode;
        set => _outputMode = value;
    }

    public async Task CopyToClipboardAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(text)) return;

        try
        {
            if (OperatingSystem.IsLinux())
            {
                var (cmd, args) = File.Exists("/usr/bin/wl-copy") || Which("wl-copy") != null
                    ? ("wl-copy", string.Empty)
                    : ("xclip", "-selection clipboard -i");

                await RunAsync(cmd, text, args, ct);
            }
            else if (OperatingSystem.IsWindows())
            {
                // PowerShell Set-Clipboard reads from stdin — no escaping issues
                await RunAsync(
                    "powershell.exe", text, "-NoProfile -NonInteractive -Command $input | Set-Clipboard", ct);
            }
            else
            {
                logger.LogWarning("Clipboard copy not supported on {OS}", Environment.OSVersion.Platform);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Clipboard copy failed — text may need manual paste");
        }
    }

    public async Task TypeTextAsync(string text, int delayBetweenKeys = 0, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(text)) return;

        var ok = await textInjection.InjectTextAsync(text, useClipboard: false);
        if (!ok)
        {
            // SendInput can be blocked by UIPI (elevated target window), a focus
            // change mid-transcription, or a locked session. Never drop the user's
            // transcription — put it on the clipboard so it can still be pasted.
            logger.LogWarning("Keystroke injection blocked — falling back to clipboard copy");
            await CopyToClipboardAsync(text, ct);
        }
    }

    // Test seam: lets unit tests observe the clipboard fallback without platform tools.
    protected virtual Task RunAsync(string cmd, string stdin, string args, CancellationToken ct)
        => RunAsyncCore(cmd, stdin, args, ct);

    private async Task RunAsyncCore(string cmd, string stdin, string args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = cmd,
            Arguments = args,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start {cmd}");

        await proc.StandardInput.WriteLineAsync(stdin);
        proc.StandardInput.Close();
        await proc.WaitForExitAsync(ct);
    }

    private static string? Which(string tool)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrEmpty(dir)) continue;
            var candidate = Path.Combine(dir.Trim(), tool);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
