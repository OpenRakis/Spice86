namespace Spice86.Converters;

using Avalonia.Data.Converters;

using System.Globalization;

internal class NullToDoubleConverter : IValueConverter {
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        if (value is null) {
            return 1;
        }
        return value.ToString();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
        if (value is string str && double.TryParse(str, out double d)) {
            return d;
        }
        return 1d;
    }
}