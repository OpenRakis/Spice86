namespace Spice86.Views.Converters;

using Avalonia.Data.Converters;

using System;
using System.Globalization;

public class BoolToOnOffConverter : IValueConverter {
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        if (value is bool isOn) {
            return isOn ? "ON" : "OFF";
        }
        return "OFF";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
        throw new NotSupportedException();
    }
}
