namespace Spice86.Converters;

using System.Globalization;

using Avalonia.Data.Converters;

internal class CountToBooleanConverter : IValueConverter {
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        return (value as int?) >= System.Convert.ToInt32(parameter);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
        return (value as bool?) switch {
            false => 0,
            true => 1,
            _ => (object)0,
        };
    }
}
