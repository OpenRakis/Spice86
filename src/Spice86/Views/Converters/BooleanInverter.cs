namespace Spice86.Views.Converters;

using Avalonia.Data;
using Avalonia.Data.Converters;

using System.Globalization;

/// <summary>
///     Converts a boolean to a Visibility.
/// </summary>
/// <remarks>
///     If the parameter is true, the converter inverts the boolean value.
/// </remarks>
public class BooleanInverter : IValueConverter {
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        return value is not true;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
        return BindingOperations.DoNothing;
    }
}