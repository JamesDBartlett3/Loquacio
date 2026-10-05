using WhisperDictation.Models;
using WhisperDictation.Services;

namespace WhisperDictation.Tests.Models;

public class HotkeyTypesTests
{
    [Fact]
    public void ParseHotkeyString_CtrlAltD()
    {
        var (key, modifiers) = IHotkeyService.ParseHotkeyString("Ctrl+Alt+D");
        Assert.Equal(HotkeyKey.D, key);
        Assert.True(modifiers.HasFlag(HotkeyModifiers.Control));
        Assert.True(modifiers.HasFlag(HotkeyModifiers.Alt));
    }

    [Fact]
    public void ParseHotkeyString_ShiftWinF5()
    {
        var (key, modifiers) = IHotkeyService.ParseHotkeyString("Shift+Win+F5");
        Assert.Equal(HotkeyKey.F5, key);
        Assert.True(modifiers.HasFlag(HotkeyModifiers.Shift));
        Assert.True(modifiers.HasFlag(HotkeyModifiers.Windows));
    }

    [Fact]
    public void ParseHotkeyString_None()
    {
        var (key, modifiers) = IHotkeyService.ParseHotkeyString("");
        Assert.Equal(HotkeyKey.None, key);
        Assert.Equal(HotkeyModifiers.None, modifiers);
    }

    [Fact]
    public void FormatHotkey_CtrlAltD()
    {
        var result = IHotkeyService.FormatHotkey(HotkeyKey.D, HotkeyModifiers.Control | HotkeyModifiers.Alt);
        Assert.Equal("Ctrl+Alt+D", result);
    }

    [Fact]
    public void FormatHotkey_CtrlShiftEnter()
    {
        var result = IHotkeyService.FormatHotkey(HotkeyKey.Enter, HotkeyModifiers.Control | HotkeyModifiers.Shift);
        Assert.Equal("Ctrl+Shift+Enter", result);
    }

    [Fact]
    public void FormatHotkey_None()
    {
        var result = IHotkeyService.FormatHotkey(HotkeyKey.None, HotkeyModifiers.None);
        Assert.Equal("", result);
    }

    [Fact]
    public void RoundTrip_CtrlAltV()
    {
        var formatted = "Ctrl+Alt+V";
        var (key, modifiers) = IHotkeyService.ParseHotkeyString(formatted);
        var result = IHotkeyService.FormatHotkey(key, modifiers);
        Assert.Equal(formatted, result);
    }

    [Fact]
    public void HotkeyBinding_RecordEquality()
    {
        var a = new HotkeyBinding(HotkeyKey.D, HotkeyModifiers.Control | HotkeyModifiers.Alt, "toggle");
        var b = new HotkeyBinding(HotkeyKey.D, HotkeyModifiers.Control | HotkeyModifiers.Alt, "toggle");
        Assert.Equal(a, b);
    }

    [Fact]
    public void HotkeyModifiers_FlagsWork()
    {
        var mods = HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift;
        Assert.True(mods.HasFlag(HotkeyModifiers.Control));
        Assert.True(mods.HasFlag(HotkeyModifiers.Alt));
        Assert.True(mods.HasFlag(HotkeyModifiers.Shift));
        Assert.False(mods.HasFlag(HotkeyModifiers.Windows));
    }
}
