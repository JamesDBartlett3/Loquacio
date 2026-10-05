using System.Runtime.InteropServices;

namespace WhisperDictation.Daemon.Interop;

/// <summary>
/// Concrete X11 interop implementation that delegates to LibX11 P/Invoke.
/// </summary>
public sealed class X11Interop : IX11Interop
{
    public IntPtr OpenDisplay() => LibX11.XOpenDisplay(IntPtr.Zero);
    public int CloseDisplay(IntPtr display) => LibX11.XCloseDisplay(display);
    public IntPtr DefaultRootWindow(IntPtr display) => LibX11.XDefaultRootWindow(display);
    public int SetErrorHandler(X11ErrorHandlerDelegate? handler) =>
        handler is null
            ? LibX11.XSetErrorHandler(null!)
            : LibX11.XSetErrorHandler(new LibX11.XErrorHandler(handler));
    public int Sync(IntPtr display, bool discard) => LibX11.XSync(display, discard);
    public int Flush(IntPtr display) => LibX11.XFlush(display);
    public int Pending(IntPtr display) => LibX11.XPending(display);
    public int NextEvent(IntPtr display, ref XEvent ev) => LibX11.XNextEvent(display, ref ev);
    public int GrabKey(IntPtr display, int keycode, uint modifiers, IntPtr grabWindow, bool ownerEvents, int pointerMode, int keyboardMode) =>
        LibX11.XGrabKey(display, keycode, modifiers, grabWindow, ownerEvents, pointerMode, keyboardMode);
    public int UngrabKey(IntPtr display, int keycode, uint modifiers, IntPtr grabWindow) =>
        LibX11.XUngrabKey(display, keycode, modifiers, grabWindow);
    public int KeysymToKeycode(IntPtr display, uint keysym) => LibX11.XKeysymToKeycode(display, keysym);
    public uint StringToKeysym(string str) => LibX11.XStringToKeysym(str);
}
