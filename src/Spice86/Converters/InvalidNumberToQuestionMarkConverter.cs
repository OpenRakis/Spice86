namespace Spice86.Converters;

using Avalonia.Data.Converters;

using System;
using System.Globalization;

internal class InvalidNumberToQuestionMarkConverter : IValueConverter {
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        if ((value is long l && l == -1) || (value is double d && d == -1)) {
            return "?";
        }
        return value;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
        if (value is string str && str == "?") {
            return -1;
        }
        if (value is string longStr && long.TryParse(longStr, out long longValue)) {
            return longValue;
        }
        return null;
    }
}
