namespace Loquacio.Services;

/// <summary>
/// Pure, OS-independent decision logic for auto-start (Phase 6.3).
/// Keeping the policy separate from registry access makes the behavior
/// unit-testable on Linux, where the registry implementation is a no-op.
/// </summary>
public static class AutoStartPolicy
{
    /// <summary>HKCU Run key that Windows reads at login.</summary>
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>Value name used inside the Run key.</summary>
    public const string ValueName = "Loquacio";

    /// <summary>
    /// Decides whether a registry transition is needed.
    /// Returns null when desired and current agree (no-op),
    /// true when auto-start must be enabled, false when it must be disabled.
    /// </summary>
    public static bool? ComputeTransition(bool desired, bool current)
        => desired == current ? null : desired;

    /// <summary>
    /// Builds the Run-key command for an executable path, always quoted,
    /// with any surrounding whitespace or stray quotes stripped.
    /// </summary>
    public static string BuildRunValue(string exePath)
    {
        var clean = (exePath ?? string.Empty).Trim().Trim('"').Trim();
        return $"\"{clean}\"";
    }
}
