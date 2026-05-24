namespace Spice86.Views.Converters;

using Avalonia.Data;
using Avalonia.Data.Converters;

using System;
using System.Globalization;

/// <summary>
/// Converts a boolean to an opacity value: <see langword="true"/> maps to 1.0, otherwise 0.15.
/// Used for activity LEDs that fade rather than fully disappear when idle.
/// </summary>
public sealed class BoolToOpacityConverter : IValueConverter {
    /// <summary>Gets the shared singleton instance.</summary>
    public static readonly BoolToOpacityConverter Instance = new();

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        if (value is bool b && b) {
            return 1.0;
        }
        return 0.15;
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
        return BindingOperations.DoNothing;
    }
}
