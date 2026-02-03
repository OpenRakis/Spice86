namespace Spice86.Views.Converters;

using Avalonia.Data.Converters;
using System;
using System.Globalization;

public class BoolToEnabledTextConverter : IValueConverter {
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        if (value is bool isEnabled) {
            return isEnabled ? "Disable" : "Enable";
        }
        return "Enable";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
        throw new NotSupportedException();
    }
}
