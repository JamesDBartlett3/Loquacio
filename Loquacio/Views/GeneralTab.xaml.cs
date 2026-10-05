using System.Windows.Controls;
using System.Windows.Input;
using Loquacio.Models;
using Loquacio.ViewModels;

namespace Loquacio.Views;

public partial class GeneralTab : UserControl
{
    public GeneralTab() => InitializeComponent();

    private void HotkeyCaptureButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string boxName } &&
            FindName(boxName) is TextBox box)
        {
            box.Focus();
            Keyboard.Focus(box);
        }
    }

    /// <summary>
    /// While the VM is in hotkey-capture mode, translate the pressed key combo
    /// into the platform-agnostic HotkeyKey/HotkeyModifiers and hand it to the VM.
    /// </summary>
    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not GeneralTabViewModel vm || !vm.IsCapturingHotkey) return;

        e.Handled = true;

        var key = MapKey(e.Key == Key.System ? e.SystemKey : e.Key);
        var modifiers = HotkeyModifiers.None;
        var mods = Keyboard.Modifiers;
        if (mods.HasFlag(ModifierKeys.Control)) modifiers |= HotkeyModifiers.Control;
        if (mods.HasFlag(ModifierKeys.Alt)) modifiers |= HotkeyModifiers.Alt;
        if (mods.HasFlag(ModifierKeys.Shift)) modifiers |= HotkeyModifiers.Shift;
        if (mods.HasFlag(ModifierKeys.Windows)) modifiers |= HotkeyModifiers.Windows;

        vm.CaptureKey(key, modifiers);
    }

    /// <summary>WPF Key names match HotkeyKey names for everything we support.</summary>
    private static HotkeyKey MapKey(Key key) => key switch
    {
        Key.None or Key.System => HotkeyKey.None,
        _ => Enum.TryParse(key.ToString(), out HotkeyKey mapped) ? mapped : HotkeyKey.None,
    };
}
