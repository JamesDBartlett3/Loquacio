using Microsoft.Win32;

namespace Loquacio.Services;

/// <summary>
/// Windows HKCU Run-key implementation of <see cref="IAutoStartService"/>.
/// On non-Windows platforms every operation is a guarded no-op (returns
/// false / false) so the app and tests stay cross-platform.
/// </summary>
public sealed class RegistryAutoStartService : IAutoStartService
{
    public bool IsEnabled
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return false;
            using var key = Registry.CurrentUser.OpenSubKey(AutoStartPolicy.RunKeyPath);
            return key?.GetValue(AutoStartPolicy.ValueName) is not null;
        }
    }

    public bool Enable()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe)) return false;
            using var key = Registry.CurrentUser.CreateSubKey(AutoStartPolicy.RunKeyPath);
            key?.SetValue(AutoStartPolicy.ValueName, AutoStartPolicy.BuildRunValue(exe));
            return key is not null;
        }
        catch
        {
            return false;
        }
    }

    public bool Disable()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AutoStartPolicy.RunKeyPath, writable: true);
            key?.DeleteValue(AutoStartPolicy.ValueName, throwOnMissingValue: false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool Toggle()
    {
        var enable = !IsEnabled;
        var ok = enable ? Enable() : Disable();
        return enable && ok;
    }
}
