using System.Globalization;
using System.Windows.Data;

namespace Loquacio.Converters;

public class InvertBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : !System.Convert.ToBoolean(value);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : !System.Convert.ToBoolean(value);
}

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is System.Windows.Visibility.Visible;
}

/// <summary>
/// Shows an element when the bound int equals the ConverterParameter (used to
/// switch content panels from the sidebar nav selection index).
/// </summary>
public class IndexEqualsToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var current = value is int i ? i : 0;
        var target = int.Parse(parameter?.ToString() ?? "0", CultureInfo.InvariantCulture);
        return current == target ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
