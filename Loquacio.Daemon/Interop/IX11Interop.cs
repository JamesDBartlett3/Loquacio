namespace Loquacio.Daemon.Interop;

/// <summary>
/// Abstraction over X11 P/Invoke calls to enable unit testing.
/// </summary>
public interface IX11Interop
{
    IntPtr OpenDisplay();
    int CloseDisplay(IntPtr display);
    IntPtr DefaultRootWindow(IntPtr display);
    int SetErrorHandler(X11ErrorHandlerDelegate? handler);
    int Sync(IntPtr display, bool discard);
    int Flush(IntPtr display);
    int Pending(IntPtr display);
    int NextEvent(IntPtr display, ref XEvent ev);
    int GrabKey(IntPtr display, int keycode, uint modifiers, IntPtr grabWindow, bool ownerEvents, int pointerMode, int keyboardMode);
    int UngrabKey(IntPtr display, int keycode, uint modifiers, IntPtr grabWindow);
    int KeysymToKeycode(IntPtr display, uint keysym);
    uint StringToKeysym(string str);
}

/// <summary>
/// Error handler delegate for X11 errors.
/// </summary>
public delegate int X11ErrorHandlerDelegate(IntPtr display, ref XErrorEvent ev);
