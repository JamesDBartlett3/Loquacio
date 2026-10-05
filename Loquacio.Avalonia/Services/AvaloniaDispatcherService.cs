using Avalonia;
using Avalonia.Threading;

namespace Loquacio.Avalonia.Services;

/// <summary>
/// Avalonia implementation of IDispatcherService.
/// Uses Avalonia's Dispatcher.UIThread for marshalling calls to the UI thread.
/// </summary>
public sealed class AvaloniaDispatcherService : IDispatcherService
{
    public bool IsOnUiThread => Dispatcher.UIThread.CheckAccess();

    public void BeginInvoke(Action action)
    {
        Dispatcher.UIThread.Post(action);
    }
}
