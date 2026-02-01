using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace Spice86.Views.Converters;

public class BoolToZeroOneConverter : IValueConverter {
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        if (value is bool b) {
            return b ? "1" : "0";
        }
        return "0";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
        throw new NotImplementedException();
    }
}
