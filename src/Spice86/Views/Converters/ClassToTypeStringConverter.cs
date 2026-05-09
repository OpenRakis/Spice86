namespace Spice86.Views.Converters;

using Avalonia.Data.Converters;

using System.Globalization;

internal class ClassToTypeStringConverter : IValueConverter {
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is Exception exception ? exception.GetBaseException().GetType().ToString() : value?.ToString() ?? "";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value;
}