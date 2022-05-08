namespace Spice86.UI.Converters;

using Avalonia.Data.Converters;

using System;
using System.Globalization;

internal class InvalidNumberToQuestionMarkConverter : IValueConverter {
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        if (value is long l && l == -1) {
            return "?";
        }
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
        if (value is string str && str == "?") {
            return -1;
        }
        if (value is string longStr && long.TryParse(longStr, out var longValue)) {
            return longValue;
        }
        return null;
    }
}
