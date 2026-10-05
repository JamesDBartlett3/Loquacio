using System.Diagnostics;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;

namespace Loquacio.Daemon.Services;

/// <summary>
/// Platform-specific text injection. Shells out to xdotool/ydotool/wtype on Linux,
/// uses SendInput P/Invoke on Windows.
/// </summary>
public sealed class TextInjectionService(ILogger<TextInjectionService> logger) : ITextInjectionService
{
    public async Task<bool> InjectTextAsync(string text, bool useClipboard = true)
    {
        if (string.IsNullOrEmpty(text)) return true;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return await InjectLinuxAsync(text, useClipboard);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return await InjectMacOSAsync(text, useClipboard);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return await InjectWindowsAsync(text, useClipboard);
        }

        logger.LogWarning("Text injection not supported on {OS}", RuntimeInformation.OSDescription);
        return false;
    }

    // ── macOS ── (CGEvent via CoreGraphics P/Invoke; see packaging/macos/README.md)

    private async Task<bool> InjectMacOSAsync(string text, bool useClipboard)
    {
        if (useClipboard)
        {
            // pbcopy sets the clipboard; then Cmd+V via CGEvent posts the paste.
            if (!await SetClipboardMacOSAsync(text))
            {
                logger.LogWarning("macOS clipboard set failed (pbcopy) — aborting clipboard injection");
                return false;
            }
            await Task.Delay(50); // let the pasteboard settle
            return PostMacOSKeyPress(MAC_KEYCODE_CMD, keyDown: true)
                && PostMacOSKeyPress(MAC_KEYCODE_V, keyDown: true)
                && PostMacOSKeyPress(MAC_KEYCODE_V, keyDown: false)
                && PostMacOSKeyPress(MAC_KEYCODE_CMD, keyDown: false);
        }

        // Direct Unicode typing via CGEventKeyboardSetUnicodeString — layout-independent.
        foreach (var part in SplitMacOSUnicodeChunks(text))
        {
            if (part.IsReturn)
            {
                if (!PostMacOSReturn()) return false;
            }
            else if (!PostMacOSUnicode(part.Text))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Splits text for the CGEvent unicode path: chunks of ≤20 UTF-16 code units
    /// (single-event limit), with '\n' mapped to Return keystrokes ('\r' dropped).
    /// </summary>
    internal static IReadOnlyList<(string Text, bool IsReturn)> SplitMacOSUnicodeChunks(string text)
    {
        var parts = new List<(string, bool)>();
        const int chunkSize = 20;
        int i = 0;
        while (i < text.Length)
        {
            int nl = text.IndexOf('\n', i);
            int end = Math.Min(i + chunkSize, text.Length);
            if (nl >= 0 && nl < end) end = nl;
            if (i < end)
            {
                // Drop '\r' (CRLF newlines) inside chunks
                var chunk = text[i..end].Replace("\r", "");
                if (chunk.Length > 0) parts.Add((chunk, false));
            }
            if (nl == end)
            {
                parts.Add(("", true));
                i = end + 1;
            }
            else
            {
                i = end;
            }
        }
        return parts;
    }

    private async Task<bool> SetClipboardMacOSAsync(string text)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "pbcopy",
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc is null) return false;
            await proc.StandardInput.WriteAsync(text);
            await proc.StandardInput.FlushAsync();
            proc.StandardInput.Close();
            await proc.WaitForExitAsync();
            return proc.ExitCode == 0;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "pbcopy failed");
            return false;
        }
    }

    private static bool PostMacOSUnicode(string chunk)
    {
        if (chunk.Length == 0) return true;
        var down = CGEventCreateKeyboardEvent(IntPtr.Zero, 0, keyDown: true);
        var up = CGEventCreateKeyboardEvent(IntPtr.Zero, 0, keyDown: false);
        if (down == IntPtr.Zero || up == IntPtr.Zero)
        {
            if (down != IntPtr.Zero) CFRelease(down);
            if (up != IntPtr.Zero) CFRelease(up);
            return false;
        }
        try
        {
            CGEventKeyboardSetUnicodeString(down, chunk.Length, chunk);
            CGEventKeyboardSetUnicodeString(up, chunk.Length, chunk);
            CGEventPost(kCGHIDEventTap, down);
            CGEventPost(kCGHIDEventTap, up);
            return true;
        }
        finally
        {
            CFRelease(down);
            CFRelease(up);
        }
    }

    private static bool PostMacOSKeyPress(ushort keyCode, bool keyDown)
    {
        var evt = CGEventCreateKeyboardEvent(IntPtr.Zero, keyCode, keyDown);
        if (evt == IntPtr.Zero) return false;
        try { CGEventPost(kCGHIDEventTap, evt); return true; }
        finally { CFRelease(evt); }
    }

    private static bool PostMacOSReturn() =>
        PostMacOSKeyPress(MAC_KEYCODE_RETURN, keyDown: true) && PostMacOSKeyPress(MAC_KEYCODE_RETURN, keyDown: false);

    // macOS virtual keycodes (HIToolbox Events.h): 36=Return, 9=V, 55=Command
    private const ushort MAC_KEYCODE_RETURN = 36;
    private const ushort MAC_KEYCODE_V = 9;
    private const ushort MAC_KEYCODE_CMD = 55;

    private const uint kCGHIDEventTap = 0;

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern IntPtr CGEventCreateKeyboardEvent(IntPtr source, ushort virtualKey, bool keyDown);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern void CGEventKeyboardSetUnicodeString(IntPtr eventRef, int length, [MarshalAs(UnmanagedType.LPWStr)] string unicodeString);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern void CGEventPost(uint tapLocation, IntPtr eventRef);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRelease(IntPtr cf);

    // ── Linux ──

    private async Task<bool> InjectLinuxAsync(string text, bool useClipboard)
    {
        // Try wtype first (Wayland-native), then ydotool (Wayland), then xdotool (X11)
        var (cmd, args) = useClipboard
            ? GetClipboardPasteCommand()
            : ("wtype", $"-d 0 -- \"{EscapeForShell(text)}\"");

        // For clipboard mode: copy text to clipboard, then simulate Ctrl+V
        if (useClipboard)
        {
            // Set clipboard contents using xclip/wl-copy
            await SetClipboardLinuxAsync(text);
            (cmd, args) = GetClipboardPasteCommand();
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = cmd,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return false;
            await proc.WaitForExitAsync();
            return proc.ExitCode == 0;
        }
        catch (FileNotFoundException ex)
        {
            logger.LogWarning(ex, "Text injection tool not found: {Cmd}. Install xdotool, ydotool, or wtype.", cmd);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Text injection failed via {Cmd}", cmd);
            return false;
        }
    }

    private static (string cmd, string args) GetClipboardPasteCommand()
    {
        // ydotool works on both X11 and Wayland
        if (File.Exists("/usr/bin/ydotool"))
            return ("ydotool", "key 29:1 47:1 47:0 29:0"); // Ctrl+V

        // xdotool (X11 only)
        if (File.Exists("/usr/bin/xdotool"))
            return ("xdotool", "key ctrl+v");

        // wtype (Wayland, keyboard input only — no key combos)
        return ("wtype", "-M ctrl -k v -m ctrl");
    }

    private async Task SetClipboardLinuxAsync(string text)
    {
        // Try wl-copy (Wayland) first, then xclip (X11)
        var attempts = new[]
        {
            ("wl-copy", new[] { "-n" }),
            ("xclip", new[] { "-selection", "clipboard" }),
            ("xsel", new[] { "--clipboard", "--input" })
        };

        foreach (var (cmd, args) in attempts)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = cmd,
                    RedirectStandardInput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                foreach (var a in args) psi.ArgumentList.Add(a);

                using var proc = Process.Start(psi);
                if (proc == null) continue;
                await proc.StandardInput.WriteAsync(text);
                await proc.StandardInput.FlushAsync();
                proc.StandardInput.Close();
                await proc.WaitForExitAsync();
                return; // Success
            }
            catch (FileNotFoundException) { /* try next */ }
        }

        logger.LogWarning("No clipboard utility found (wl-copy, xclip, xsel)");
    }

    private static string EscapeForShell(string text) => text.Replace("\"", "\\\"");

    // ── Windows ──

    private async Task<bool> InjectWindowsAsync(string text, bool useClipboard)
    {
        if (useClipboard)
        {
            // Copy to clipboard via Win32 (fast, no shell quoting issues), then SendInput Ctrl+V
            try
            {
                if (!SetClipboardText(text))
                {
                    logger.LogWarning("Win32 clipboard set failed — aborting clipboard injection");
                    return false;
                }

                // Give the clipboard a moment to settle before pasting
                await Task.Delay(50);
                return SendKeyCombo(VK_CONTROL, VK_V);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Windows clipboard injection failed");
                return false;
            }
        }

        // Direct keystroke injection via SendInput with KEYEVENTF_UNICODE.
        // Works for arbitrary Unicode text regardless of the active keyboard layout.
        return SendUnicodeText(text);
    }

    /// <summary>
    /// Types arbitrary Unicode text into the focused window using SendInput
    /// with KEYEVENTF_UNICODE. Surrogate pairs are sent down,down,up,up so the
    /// target app reassembles the astral character. Newlines become Return keystrokes.
    /// </summary>
    private bool SendUnicodeText(string text)
    {
        var inputs = new List<INPUT>(text.Length * 2);

        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (ch == '\r') continue;
            if (ch == '\n')
            {
                inputs.Add(KeyInput(VK_RETURN, isVirtualKey: true, keyUp: false));
                inputs.Add(KeyInput(VK_RETURN, isVirtualKey: true, keyUp: true));
                continue;
            }

            // Surrogate pair: down,down,up,up — the robust convention for
            // KEYEVENTF_UNICODE so apps reassemble the astral character.
            if (char.IsHighSurrogate(ch) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                inputs.Add(KeyInput((ushort)ch, isVirtualKey: false, keyUp: false));
                inputs.Add(KeyInput((ushort)text[++i], isVirtualKey: false, keyUp: false));
                inputs.Add(KeyInput((ushort)text[i - 1], isVirtualKey: false, keyUp: true));
                inputs.Add(KeyInput((ushort)text[i], isVirtualKey: false, keyUp: true));
                continue;
            }

            inputs.Add(KeyInput((ushort)ch, isVirtualKey: false, keyUp: false));
            inputs.Add(KeyInput((ushort)ch, isVirtualKey: false, keyUp: true));
        }

        if (inputs.Count == 0) return true;

        var batch = inputs.ToArray();
        // SendInput can't take more than ~62KB in one call; chunk defensively
        const int maxPerCall = 512;
        for (int i = 0; i < batch.Length; i += maxPerCall)
        {
            var slice = batch[i..Math.Min(i + maxPerCall, batch.Length)];
            var sent = SendInput((uint)slice.Length, slice, Marshal.SizeOf<INPUT>());
            if (sent != slice.Length)
            {
                logger.LogError(
                    "SendInput failed: injected {Sent}/{Total} keyboard events (Win32 error {Error}). " +
                    "This is expected for elevated (admin) target windows (UIPI); otherwise check the INPUT struct size.",
                    sent, slice.Length, Marshal.GetLastWin32Error());
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Size of the marshalled INPUT struct. Must equal the Win32 sizeof(INPUT)
    /// (40 bytes on x64 — the union is as large as MOUSEINPUT even when only
    /// KEYBDINPUT is used), otherwise SendInput rejects every call with
    /// ERROR_INVALID_PARAMETER. Exposed for a regression test.
    /// </summary>
    internal static int InputStructSize => Marshal.SizeOf<INPUT>();

    private static INPUT KeyInput(ushort key, bool isVirtualKey, bool keyUp)
    {
        uint flags = isVirtualKey ? 0 : KEYEVENTF_UNICODE;
        if (keyUp) flags |= KEYEVENTF_KEYUP;
        return new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = isVirtualKey ? key : (ushort)0,
                    wScan = isVirtualKey ? (ushort)0 : key,
                    dwFlags = flags
                }
            }
        };
    }

    /// <summary>Sets the clipboard to Unicode text via Win32 (no shell-out, no quoting bugs).</summary>
    private static bool SetClipboardText(string text)
    {
        if (!OpenClipboard(IntPtr.Zero)) return false;
        try
        {
            if (!EmptyClipboard()) return false;

            var bytes = (text.Length + 1) * 2;
            var hGlobal = GlobalAlloc(GMEM_MOVEABLE, (nuint)bytes);
            if (hGlobal == IntPtr.Zero) return false;

            var ptr = GlobalLock(hGlobal);
            if (ptr == IntPtr.Zero)
            {
                GlobalFree(hGlobal);
                return false;
            }

            try
            {
                Marshal.Copy(text.ToCharArray(), 0, ptr, text.Length);
                Marshal.WriteInt16(ptr, text.Length * 2, 0); // null terminator
            }
            finally
            {
                GlobalUnlock(hGlobal);
            }

            if (SetClipboardData(CF_UNICODETEXT, hGlobal) == IntPtr.Zero)
            {
                GlobalFree(hGlobal);
                return false;
            }
            // System now owns hGlobal on success — do not free it.
            return true;
        }
        finally
        {
            CloseClipboard();
        }
    }

    // Win32 SendInput constants
    private const int VK_CONTROL = 0x11;
    private const int VK_V = 0x56;
    private const ushort VK_RETURN = 0x0D;

    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint uFlags, nuint dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr hMem);

    private bool SendKeyCombo(params int[] keys)
    {
        // Simplified: press all keys down, then release in reverse
        var inputs = new List<INPUT>();
        foreach (var key in keys)
        {
            inputs.Add(new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = (ushort)key,
                        dwFlags = 0
                    }
                }
            });
        }
        for (int i = keys.Length - 1; i >= 0; i--)
        {
            inputs.Add(new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = (ushort)keys[i],
                        dwFlags = KEYEVENTF_KEYUP
                    }
                }
            });
        }

        var result = SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
        return result == inputs.Count;
    }

    private const int INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUT
    {
        [FieldOffset(0)] public int type;
        [FieldOffset(8)] public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }
}
