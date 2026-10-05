using Microsoft.Win32;
using Microsoft.Extensions.Logging;

namespace WhisperDictation.Services;

/// <summary>
/// Manages auto-start with Windows via HKCU Run key.
/// </summary>
public class AutoStartService : IAutoStartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "WhisperDictation";
    private readonly ILogger<AutoStartService> _logger;

    public AutoStartService(ILogger<AutoStartService> logger)
    {
        _logger = logger;
    }

    public bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
                return key?.GetValue(AppName) != null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to check auto-start status");
                return false;
            }
        }
    }

    public bool Enable()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath))
            {
                _logger.LogError("Could not determine executable path for auto-start");
                return false;
            }

            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key == null)
            {
                _logger.LogError("Could not open registry Run key for writing");
                return false;
            }

            key.SetValue(AppName, $"\"{exePath}\" --minimized");
            _logger.LogInformation("Auto-start enabled: {Path}", exePath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enable auto-start");
            return false;
        }
    }

    public bool Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key == null)
            {
                _logger.LogError("Could not open registry Run key for writing");
                return false;
            }

            key.DeleteValue(AppName, throwOnMissingValue: false);
            _logger.LogInformation("Auto-start disabled");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to disable auto-start");
            return false;
        }
    }

    public bool Toggle()
    {
        if (IsEnabled)
        {
            Disable();
            return false;
        }
        else
        {
            Enable();
            return true;
        }
    }
}
