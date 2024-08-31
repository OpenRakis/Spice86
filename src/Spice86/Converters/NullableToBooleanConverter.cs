namespace Spice86.Converters;

using Avalonia.Data;
using Avalonia.Data.Converters;

using System.Globalization;

public class NullableToBooleanConverter : IValueConverter {
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        return value is not null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
        return BindingOperations.DoNothing;
    }
}