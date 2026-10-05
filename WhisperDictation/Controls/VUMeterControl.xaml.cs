using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WhisperDictation.Controls;

public partial class VUMeterControl : UserControl
{
    public static readonly DependencyProperty LevelProperty =
        DependencyProperty.Register(nameof(Level), typeof(double), typeof(VUMeterControl),
            new PropertyMetadata(0.0, OnLevelChanged));

    public double Level
    {
        get => (double)GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    public static readonly DependencyProperty PeakLevelProperty =
        DependencyProperty.Register(nameof(PeakLevel), typeof(double), typeof(VUMeterControl),
            new PropertyMetadata(0.0, OnLevelChanged));

    public double PeakLevel
    {
        get => (double)GetValue(PeakLevelProperty);
        set => SetValue(PeakLevelProperty, value);
    }

    public static readonly DependencyProperty SilenceThresholdDbProperty =
        DependencyProperty.Register(nameof(SilenceThresholdDb), typeof(double), typeof(VUMeterControl),
            new PropertyMetadata(-40.0, OnLevelChanged));

    public double SilenceThresholdDb
    {
        get => (double)GetValue(SilenceThresholdDbProperty);
        set => SetValue(SilenceThresholdDbProperty, value);
    }

    public static readonly DependencyProperty CompressorThresholdDbProperty =
        DependencyProperty.Register(nameof(CompressorThresholdDb), typeof(double), typeof(VUMeterControl),
            new PropertyMetadata(-18.0, OnLevelChanged));

    public double CompressorThresholdDb
    {
        get => (double)GetValue(CompressorThresholdDbProperty);
        set => SetValue(CompressorThresholdDbProperty, value);
    }

    public VUMeterControl()
    {
        InitializeComponent();
        SizeChanged += (_, _) => Redraw();
    }

    private static void OnLevelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((VUMeterControl)d).Redraw();
    }

    private void Redraw()
    {
        var canvas = MeterCanvas;
        canvas.Children.Clear();

        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0)
            return;

        const double minDb = -60;
        const double maxDb = 0;
        var levelDb = Level > 0 ? 20 * Math.Log10(Level) : minDb;
        var peakDb = PeakLevel > 0 ? 20 * Math.Log10(PeakLevel) : minDb;
        var level = Math.Clamp((levelDb - minDb) / (maxDb - minDb), 0.0, 1.0);
        var peak = Math.Clamp((peakDb - minDb) / (maxDb - minDb), 0.0, 1.0);

        // Background segments
        var segmentCount = 120;
        var segmentWidth = width / segmentCount;
        for (var i = 0; i < segmentCount; i++)
        {
            var x = i * segmentWidth;
            var lit = (double)i / segmentCount < level;

            Color color;
            if ((double)i / segmentCount < 0.6)
                color = lit ? Colors.Green : Color.FromArgb(30, 0, 128, 0);
            else if ((double)i / segmentCount < 0.85)
                color = lit ? Colors.Yellow : Color.FromArgb(30, 128, 128, 0);
            else
                color = lit ? Colors.Red : Color.FromArgb(30, 128, 0, 0);

            var rect = new System.Windows.Shapes.Rectangle
            {
                Width = Math.Max(0.1, segmentWidth - 0.25),
                Height = Math.Max(0.1, height - 2),
                Fill = new SolidColorBrush(color),
                Margin = new Thickness(x + 0.125, 1, 0, 0)
            };
            canvas.Children.Add(rect);
        }

        if (width > 0 && height > 0)
        {
            canvas.Children.Add(new System.Windows.Shapes.Rectangle
            {
                Width = 2,
                Height = height,
                Fill = TryFindResource("TextFillColorPrimaryBrush") as Brush ?? Brushes.White,
                Margin = new Thickness(Math.Max(0, width * peak - 1), 0, 0, 0)
            });

            foreach (var thresholdDb in new[] { SilenceThresholdDb, CompressorThresholdDb })
            {
                var thresholdPosition = Math.Clamp((thresholdDb - minDb) / (maxDb - minDb), 0.0, 1.0);
                canvas.Children.Add(new System.Windows.Shapes.Rectangle
                {
                    Width = 1,
                    Height = height,
                    Fill = thresholdDb == SilenceThresholdDb
                        ? TryFindResource("AccentFillColorDefaultBrush") as Brush ?? Brushes.Cyan
                        : TryFindResource("SystemFillColorAttentionBrush") as Brush ?? Brushes.Orange,
                    Margin = new Thickness(width * thresholdPosition, 0, 0, 0)
                });
            }
        }
    }
}
