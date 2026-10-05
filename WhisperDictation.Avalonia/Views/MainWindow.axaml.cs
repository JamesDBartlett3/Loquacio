using Avalonia.Controls;
using WhisperDictation.Avalonia.ViewModels;

namespace WhisperDictation.Avalonia.Views;

public partial class MainWindow : Window
{
    private ControllerViewModel? Vm => DataContext as ControllerViewModel;

    public MainWindow()
    {
        InitializeComponent();
        // Note: InitializeAsync is called from App.OnFrameworkInitializationCompleted
        // with the daemon lifecycle service. Do NOT call it here to avoid double-init.
    }
}
