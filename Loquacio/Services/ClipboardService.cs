using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Loquacio.Services;

/// <summary>
/// Windows clipboard and keyboard simulation service.
/// Uses Win32 API for clipboard operations and SendInput for typing.
/// </summary>
public class ClipboardService : IClipboardService
{
    private readonly ILogger<ClipboardService> _logger;
    private string _outputMode = "clipboard";

    public string OutputMode
    {
        get => _outputMode;
        set => _outputMode = string.Equals(value, "type", StringComparison.OrdinalIgnoreCase) ? "type" : "clipboard";
    }

    public ClipboardService(ILogger<ClipboardService> logger)
    {
        _logger = logger;
    }

    public Task CopyToClipboardAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(text))
        {
            _logger.LogDebug("Clipboard copy skipped (empty text)");
            return Task.CompletedTask;
        }

        // Run on STA thread (clipboard requires STA)
        return Task.Run(() =>
        {
            try
            {
                SetClipboardText(text);
                _logger.LogInformation("Copied {Length} characters to clipboard", text.Length);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to copy to clipboard");
                throw;
            }
        }, ct);
    }

    public async Task TypeTextAsync(string text, int delayBetweenKeys = 0, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        _logger.LogInformation("Typing {Length} characters", text.Length);

        await Task.Run(() =>
        {
            foreach (var c in text)
            {
                ct.ThrowIfCancellationRequested();

                SendChar(c);

                if (delayBetweenKeys > 0)
                {
                    Thread.Sleep(delayBetweenKeys);
                }
            }
        }, ct);

        _logger.LogInformation("Finished typing text");
    }

    private static void SetClipboardText(string text)
    {
        // Win32 clipboard API
        // This requires the process to be running in an STA context or use OpenClipboard
        if (!OpenClipboard(IntPtr.Zero))
        {
            throw new InvalidOperationException("Failed to open clipboard");
        }

        try
        {
            EmptyClipboard();

            // Allocate global memory for the text
            var bytes = Encoding.Unicode.GetBytes(text + "\0");
            var hGlobal = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes.Length);

            if (hGlobal == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to allocate memory for clipboard");
            }

            var pGlobal = GlobalLock(hGlobal);
            if (pGlobal != IntPtr.Zero)
            {
                Marshal.Copy(bytes, 0, pGlobal, bytes.Length);
                GlobalUnlock(hGlobal);

                // Set clipboard data (CF_UNICODETEXT = 13)
                SetClipboardData(13, hGlobal);
            }
            else
            {
                GlobalFree(hGlobal);
                throw new InvalidOperationException("Failed to lock memory");
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    private static void SendChar(char c)
    {
        // Use SendInput for character input
        // For simplicity, this uses the Unicode INPUT structure
        var input = new INPUT
        {
            type = INPUT_TYPE.KEYBOARD,
            u = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = 0,
                    wScan = c,
                    dwFlags = KEYEVENTF_UNICODE,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        // Key down
        var inputs = new[] { input };
        SendInput(1, inputs, INPUT.Size);

        // Key up
        input.u.ki.dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP;
        inputs[0] = input;
        SendInput(1, inputs, INPUT.Size);
    }

    // Win32 P/Invoke declarations
    private const uint GMEM_MOVEABLE = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [DllImport("user32.dll")]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll")]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll")]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalFree(IntPtr hMem);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    // SendInput structures
    private enum INPUT_TYPE : uint
    {
        MOUSE = 0,
        KEYBOARD = 1,
        HARDWARE = 2
    }

    [Flags]
    private enum KEYEVENTF : uint
    {
        EXTENDEDKEY = 0x0001,
        KEYUP = 0x0002,
        UNICODE = 0x0004,
        SCANCODE = 0x0008
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public char wScan;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public INPUT_TYPE type;
        public InputUnion u;

        public static readonly int Size = Marshal.SizeOf<INPUT>();
    }
}
