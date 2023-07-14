namespace Spice86.Converters;

using System.Globalization;

using Avalonia.Data.Converters;

internal class ClassToTypeStringConverter : IValueConverter {
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is Exception exception ? exception.GetBaseException().GetType().ToString() : value?.ToString() ?? "";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value;
}
