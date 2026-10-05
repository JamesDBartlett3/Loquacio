namespace Loquacio.Services;

/// <summary>
/// Abstraction over UI thread dispatching.
/// WPF: wraps Application.Current.Dispatcher.
/// Avalonia: wraps Avalonia.Threading.UiThread.
/// TUI: no-op (Terminal.Gui is single-threaded).
/// </summary>
public interface IDispatcherService
{
    /// <summary>
    /// Execute an action on the UI thread (async).
    /// </summary>
    void BeginInvoke(Action action);

    /// <summary>
    /// Check if the current thread is the UI thread.
    /// </summary>
    bool IsOnUiThread { get; }
}
