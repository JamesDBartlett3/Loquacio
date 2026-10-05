using System.Windows;

namespace Loquacio.Services;

/// <summary>
/// WPF implementation of IDispatcherService using Application.Current.Dispatcher.
/// </summary>
public class WpfDispatcherService : IDispatcherService
{
    public bool IsOnUiThread =>
        Application.Current is not null &&
        !Application.Current.Dispatcher.CheckAccess();

    public void BeginInvoke(Action action)
    {
        if (Application.Current is null)
        {
            // No WPF app context — execute synchronously
            action();
            return;
        }

        if (Application.Current.Dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            Application.Current.Dispatcher.BeginInvoke(action);
        }
    }
}
