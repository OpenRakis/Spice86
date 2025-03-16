namespace Spice86.Converters;

using Avalonia.Data.Converters;
using Avalonia.Media;

using System;
using System.Globalization;

/// <summary>
/// Converts a boolean to a brush for highlighting backgrounds.
/// </summary>
public class BooleanToHighlightingBackgroundConverter : IValueConverter
{
    // Default colors for fallback
    private static readonly SolidColorBrush DarkModeHighlight = new SolidColorBrush(Color.FromRgb(40, 40, 80));
    private static readonly SolidColorBrush LightModeHighlight = new SolidColorBrush(Color.FromRgb(173, 214, 255));

    /// <summary>
    /// Converts a boolean to a brush for highlighting backgrounds.
    /// </summary>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue && boolValue)
        {
            // Return the appropriate brush based on the parameter or default to dark mode highlight
            return parameter switch
            {
                "Light" => LightModeHighlight,
                "Dark" => DarkModeHighlight,
                _ => DarkModeHighlight
            };
        }
        
        return Brushes.Transparent;
    }

    /// <summary>
    /// Not implemented.
    /// </summary>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
