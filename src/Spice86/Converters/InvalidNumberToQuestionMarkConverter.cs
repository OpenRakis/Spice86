namespace Spice86.Converters;

using Avalonia.Data.Converters;

using System;
using System.Globalization;

internal class InvalidNumberToQuestionMarkConverter : IValueConverter {
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        switch (value)
        {
            case null:
            case long l when l == -1:
            case int i when i == -1:
            case short s when s == -1:
            case sbyte b when b == -1:
            case nint n when n == -1:
            case Half h when h == -1:
            case float f when float.IsNegative(f):
            case double d when double.IsNegative(d):
                return "?";
            default:
                return value;
        }
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
