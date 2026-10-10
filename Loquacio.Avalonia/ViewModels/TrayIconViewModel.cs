using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Loquacio.Avalonia.ViewModels;

/// <summary>
/// Phase 6.1 - ViewModel backing the system tray icon.
/// Maps controller state to a color-coded status (Gray/Blue/Yellow/Red)
/// and exposes tray context-menu commands (Start/Stop, Show, Quit).
/// The actual TrayIcon rendering lives in TrayService; this class owns
/// the state and commands so the tray stays testable.
/// </summary>
public partial class TrayIconViewModel : ObservableObject
{
    private readonly ILogger<TrayIconViewModel> _logger;

    [ObservableProperty]
    private bool _isListening;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _statusText = "Idle";

    /// <summary>Color-coded status: Gray=Idle, Blue=Active, Yellow=Connecting/Processing, Red=Error.</summary>
    [ObservableProperty]
    private string _statusColor = "#808080";

    public IRelayCommand ToggleListeningCommand { get; }
    public IRelayCommand ShowWindowCommand { get; }
    public IRelayCommand QuitCommand { get; }

    public TrayIconViewModel(
        Func<Task> toggleListening,
        Action showWindow,
        Action quitApp,
        ILogger<TrayIconViewModel> logger)
    {
        _logger = logger;
        ToggleListeningCommand = new AsyncRelayCommand(toggleListening);
        ShowWindowCommand = new RelayCommand(showWindow);
        QuitCommand = new RelayCommand(quitApp);
    }

    public void UpdateState(bool isListening, bool isConnected, string statusText)
    {
        IsListening = isListening;
        IsConnected = isConnected;
        StatusText = statusText;
        StatusColor = ComputeStatusColor(isListening, isConnected, statusText);
    }

    /// <summary>Pure mapping so the color logic is unit-testable without a tray.</summary>
    public static string ComputeStatusColor(bool isListening, bool isConnected, string statusText)
    {
        if (isListening) return "#0000FF";                       // Blue - active
        if (!isConnected) return "#FF0000";                      // Red - error/disconnected
        if (statusText.Contains("Connect", StringComparison.OrdinalIgnoreCase) ||
            statusText.Contains("Process", StringComparison.OrdinalIgnoreCase)) return "#FFFF00"; // Yellow
        return "#808080";                                        // Gray - idle
    }

    public override string ToString() => "Loquacio - " + StatusText;
}
