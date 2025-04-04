namespace Spice86.Converters;

using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

/// <summary>
/// Converts a boolean WillJump value to a visual representation for jump indicators.
/// </summary>
public class JumpIndicatorConverter : IValueConverter {
    public static readonly JumpIndicatorConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        if (parameter == null) {
            return value;
        }
        // If WillJump is null, hide the indicator
        if (value is not bool willJump) {
            return (string)parameter == "visibility" ? false : null;
        }

        // Convert different properties
        return parameter switch {
            "color" => willJump ? Brushes.LightGreen : Brushes.IndianRed,
            "text" => willJump ? "TAKEN" : "NOT TAKEN",
            "visibility" => true,
            _ => null
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
        throw new NotImplementedException();
    }
}
