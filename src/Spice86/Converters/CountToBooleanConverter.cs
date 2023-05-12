namespace Spice86.Converters;

using System.Globalization;

using Avalonia.Data.Converters;

internal class CountToBooleanConverter : IValueConverter {
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        return (value as int?) >= System.Convert.ToInt32(parameter);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
        switch (value as bool?) {
            case false:
                return 0;
            case true:
                return 1;
            default:
                return 0;
        }
    }
}
