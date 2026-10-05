using System.Globalization;
using System.Windows.Data;

namespace WhisperDictation.Converters;

/// <summary>Hotkey capture buttons: idle → "Capture", while capturing → "Cancel".</summary>
public class BoolToCaptureConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? "Cancel" : "Capture";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
