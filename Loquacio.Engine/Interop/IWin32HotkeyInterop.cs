using System.Runtime.InteropServices;

namespace Loquacio.Engine.Interop;

/// <summary>
/// Seam over the Win32 hotkey APIs (RegisterHotKey + message pump), mirroring
/// <see cref="IX11Interop"/>. Lets the Win32 hotkey path in HotkeyManager be
/// exercised by unit tests on any OS without touching user32.dll — same pattern
/// as the Porcupine engine seam in Loquacio.Core.
/// </summary>
public interface IWin32HotkeyInterop
{
    IntPtr GetModuleHandle(string? moduleName);
    IntPtr CreateMessageOnlyWindow(string className);
    bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint virtualKey);
    bool UnregisterHotKey(IntPtr hwnd, int id);
    bool DestroyWindow(IntPtr hwnd);
    int GetMessage(out MSG msg, IntPtr hwnd, uint filterMin, uint filterMax);
    bool TranslateMessage(ref MSG msg);
    IntPtr DispatchMessage(ref MSG msg);
    bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);
    uint GetCurrentThreadId();
}

/// <summary>
/// Default implementation backed by the static Win32 P/Invoke bindings.
/// </summary>
public sealed class Win32HotkeyInterop : IWin32HotkeyInterop
{
    public IntPtr GetModuleHandle(string? moduleName) => Win32.GetModuleHandle(moduleName);

    public IntPtr CreateMessageOnlyWindow(string className) => Win32.CreateWindowEx(
        0, className, "", 0,
        0, 0, 0, 0,
        (IntPtr)(-3), // HWND_MESSAGE — message-only window
        IntPtr.Zero, Win32.GetModuleHandle(null), IntPtr.Zero);

    public bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint virtualKey)
        => Win32.RegisterHotKey(hwnd, id, modifiers, virtualKey);

    public bool UnregisterHotKey(IntPtr hwnd, int id) => Win32.UnregisterHotKey(hwnd, id);

    public bool DestroyWindow(IntPtr hwnd) => Win32.DestroyWindow(hwnd);

    public int GetMessage(out MSG msg, IntPtr hwnd, uint filterMin, uint filterMax)
        => Win32.GetMessage(out msg, hwnd, filterMin, filterMax);

    public bool TranslateMessage(ref MSG msg) => Win32.TranslateMessage(ref msg);

    public IntPtr DispatchMessage(ref MSG msg) => Win32.DispatchMessage(ref msg);

    public bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam)
        => Win32.PostThreadMessage(threadId, msg, wParam, lParam);

    public uint GetCurrentThreadId() => Win32.GetCurrentThreadId();
}

