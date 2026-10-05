using System.Runtime.InteropServices;

namespace WhisperDictation.Daemon.Interop;

/// <summary>
/// Win32 P/Invoke bindings for global hotkey registration via RegisterHotKey.
/// Used on Windows where a hidden message-only window processes WM_HOTKEY messages.
///
/// On Linux, these are never called (guarded by RuntimeInformation.IsOSPlatform).
/// The DllImport to user32.dll is safe — .NET only resolves the native library
/// when a method is actually invoked.
/// </summary>
internal static class Win32
{
    private const string User32 = "user32.dll";

    // Window message constants
    internal const int WM_HOTKEY = 0x0312;
    internal const int WM_DESTROY = 0x0002;
    internal const int WM_QUIT = 0x0012;

    // Window creation
    internal const int WS_EX_MESSAGEBOX = 0x00000080; // Message-only window (HWND_MESSAGE)

    [DllImport(User32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport(User32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport(User32, SetLastError = true)]
    internal static extern IntPtr CreateWindowEx(
        uint dwExStyle, string lpClassName, string lpWindowName,
        uint dwStyle, int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport(User32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport(User32)]
    internal static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport(User32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32")]
    internal static extern uint GetCurrentThreadId();

    [DllImport(User32)]
    internal static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport(User32)]
    internal static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("kernel32", SetLastError = true)]
    internal static extern IntPtr GetModuleHandle(string? lpModuleName);

    // Win32 modifier constants (different from X11 masks!)
    internal const uint MOD_ALT = 0x0001;
    internal const uint MOD_CONTROL = 0x0002;
    internal const uint MOD_SHIFT = 0x0004;
    internal const uint MOD_WIN = 0x0008;
    internal const uint MOD_NOREPEAT = 0x4000;

    // Common Win32 virtual-key codes
    internal const uint VK_F1 = 0x70;
    internal const uint VK_F2 = 0x71;
    internal const uint VK_F3 = 0x72;
    internal const uint VK_F4 = 0x73;
    internal const uint VK_F5 = 0x74;
    internal const uint VK_F6 = 0x75;
    internal const uint VK_F7 = 0x76;
    internal const uint VK_F8 = 0x77;
    internal const uint VK_F9 = 0x78;
    internal const uint VK_F10 = 0x79;
    internal const uint VK_F11 = 0x7A;
    internal const uint VK_F12 = 0x7B;
    internal const uint VK_SPACE = 0x20;
    internal const uint VK_RETURN = 0x0D;
    internal const uint VK_ESCAPE = 0x1B;
    internal const uint VK_BACK = 0x08;
    internal const uint VK_TAB = 0x09;
    // Letters A-Z are 0x41-0x5A, digits 0-9 are 0x30-0x39
}

/// <summary>
/// Win32 MSG structure for message pump.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct MSG
{
    public IntPtr hWnd;
    public uint message;
    public IntPtr wParam;
    public IntPtr lParam;
    public uint time;
    public int pt_x;
    public int pt_y;
}
