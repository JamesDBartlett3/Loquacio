using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Loquacio.Avalonia.Views;

/// <summary>
/// Simple horizontal VU meter that fills based on audio level (0.0–1.0).
/// </summary>
public partial class VuMeterControl : UserControl
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<VuMeterControl, double>(nameof(Value));

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    private Border? _fillBar;
    private double _peak;
    private DateTime _lastPeakTime = DateTime.MinValue;

    public VuMeterControl()
    {
        InitializeComponent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty)
        {
            UpdateFill();
        }
    }

    private void UpdateFill()
    {
        if (_fillBar == null)
        {
            _fillBar = this.FindControl<Border>("FillBar");
            if (_fillBar == null) return;
        }

        var level = Math.Clamp(Value, 0, 1);
        var width = Bounds.Width * level;
        _fillBar.Width = Math.Max(0, width);

        // Color gradient: green → yellow → red
        if (level < 0.6)
            _fillBar.Background = new SolidColorBrush(Color.Parse("#22c55e"));
        else if (level < 0.85)
            _fillBar.Background = new SolidColorBrush(Color.Parse("#eab308"));
        else
            _fillBar.Background = new SolidColorBrush(Color.Parse("#ef4444"));

        // Peak hold
        if (level > _peak)
        {
            _peak = level;
            _lastPeakTime = DateTime.Now;
        }
        else if ((DateTime.Now - _lastPeakTime).TotalMilliseconds > 800)
        {
            _peak = Math.Max(0, _peak - 0.02);
        }

        var peakBar = this.FindControl<Border>("PeakBar");
        if (peakBar != null)
        {
            peakBar.Margin = new Thickness(Bounds.Width * Math.Clamp(_peak, 0, 1) - 1, 0, 0, 0);
        }
    }
}
