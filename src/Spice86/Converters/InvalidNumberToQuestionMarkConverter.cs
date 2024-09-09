namespace Spice86.Converters;

using Avalonia.Data.Converters;

using System;
using System.Globalization;

/// <summary>
/// Converts invalid number values (null, -1, negative float or double, etc.) to question marks ("?").
/// </summary>
internal class InvalidNumberToQuestionMarkConverter : IValueConverter {
    /// <inheritdoc/>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        switch (value) {
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

    /// <inheritdoc/>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
        return value switch {
            string and "?" => -1,
            string longStr when long.TryParse(longStr, out long longValue) => longValue,
            _ => null
        };
    }
}