using System.Runtime.InteropServices;

namespace WhisperDictation.Daemon.Interop;

/// <summary>
/// Minimal X11 P/Invoke bindings for global hotkey registration via XGrabKey.
/// Only includes the functions and constants needed for hotkey support.
/// </summary>
internal static class LibX11
{
    private const string LibName = "libX11.so.6";

    // Display open/close
    [DllImport(LibName)]
    internal static extern IntPtr XOpenDisplay(IntPtr display);

    [DllImport(LibName)]
    internal static extern int XCloseDisplay(IntPtr display);

    // Root window
    [DllImport(LibName)]
    internal static extern IntPtr XDefaultRootWindow(IntPtr display);

    // Error handlers
    [DllImport(LibName)]
    internal static extern int XSetErrorHandler(XErrorHandler handler);

    [DllImport(LibName)]
    internal static extern int XSync(IntPtr display, bool discard);

    [DllImport(LibName)]
    internal static extern int XFlush(IntPtr display);

    // Event polling
    [DllImport(LibName)]
    internal static extern int XPending(IntPtr display);

    [DllImport(LibName)]
    internal static extern int XNextEvent(IntPtr display, ref XEvent ev);

    // Key grab
    [DllImport(LibName)]
    internal static extern int XGrabKey(
        IntPtr display, int keycode, uint modifiers,
        IntPtr grab_window, bool owner_events,
        int pointer_mode, int keyboard_mode);

    [DllImport(LibName)]
    internal static extern int XUngrabKey(
        IntPtr display, int keycode, uint modifiers,
        IntPtr grab_window);

    // Keycode lookup
    [DllImport(LibName)]
    internal static extern int XKeysymToKeycode(IntPtr display, uint keysym);

    // Key symbol string lookup
    [DllImport(LibName)]
    internal static extern uint XStringToKeysym(string str);

    // Constants
    internal const int GrabModeAsync = 1;
    internal const int KeyPress = 2;
    internal const int KeyRelease = 3;

    // Modifier masks
    internal const uint ShiftMask = 1 << 0;
    internal const uint LockMask = 1 << 1;   // CapsLock
    internal const uint ControlMask = 1 << 2;
    internal const uint Mod1Mask = 1 << 3;    // Alt
    internal const uint Mod2Mask = 1 << 4;    // NumLock
    internal const uint Mod3Mask = 1 << 5;
    internal const uint Mod4Mask = 1 << 6;    // Super (Windows key)
    internal const uint Mod5Mask = 1 << 7;    // Scroll Lock
    internal const uint AnyModifier = (1 << 15);

    // Common keysyms
    internal const uint XK_space = 0x020;
    internal const uint XK_F1 = 0xffbe;
    internal const uint XK_F2 = 0xffbf;
    internal const uint XK_F3 = 0xffc0;
    internal const uint XK_F4 = 0xffc1;
    internal const uint XK_F5 = 0xffc2;

    internal delegate int XErrorHandler(IntPtr display, ref XErrorEvent ev);
}

/// <summary>
/// X11 error event structure.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct XErrorEvent
{
    public int type;
    public IntPtr display;
    public IntPtr resourceid;
    public uint serial;
    public byte error_code;
    public byte request_code;
    public byte minor_code;
}

/// <summary>
/// X11 event structure (simplified — only KeyPress/KeyRelease fields used).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct XEvent
{
    public int type;
    public IntPtr serial;
    public bool send_event;
    public IntPtr display;
    public IntPtr window;
    public IntPtr root;
    public IntPtr subwindow;
    public uint time;
    public int x, y;
    public int x_root, y_root;
    public uint state;
    public uint keycode;
    public bool same_screen;
}
