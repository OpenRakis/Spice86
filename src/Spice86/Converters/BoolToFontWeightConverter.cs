namespace Spice86.Converters;

using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;

using System;
using System.Globalization;

internal class BoolToFontWeightConverter : IValueConverter {
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        if(value is bool source) {
            return source ? FontWeight.Bold : FontWeight.Normal;
        }
        return BindingOperations.DoNothing;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
        return BindingOperations.DoNothing;
    }
}
