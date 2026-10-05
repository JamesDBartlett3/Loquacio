using Avalonia.Data.Converters;
using Avalonia.Media;
using System.Globalization;

namespace WhisperDictation.Avalonia.Views;

/// <summary>
/// Converts IsListening bool to button text.
/// </summary>
public sealed class ListenButtonTextConverter : IValueConverter
{
    public static readonly ListenButtonTextConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? "⏹ Stop" : "▶ Start";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// Converts bool (connected/disconnected) to a Color.
/// </summary>
public sealed class BoolToBrushConverter : IValueConverter
{
    public static readonly BoolToBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? Colors.LimeGreen : Colors.Red;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
