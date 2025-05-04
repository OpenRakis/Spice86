using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace Spice86.Converters;

/// <summary>
/// Provides utility methods for converters to fetch brushes from application resources.
/// </summary>
public static class ConverterUtilities {
    /// <summary>
    /// Gets a brush from application resources with a fallback value if not found.
    /// </summary>
    /// <param name="resourceKey">The resource key to look up.</param>
    /// <param name="fallbackBrush">The fallback brush to use if the resource is not found.</param>
    /// <returns>The brush from resources or the fallback brush.</returns>
    public static IBrush GetResourceBrush(string resourceKey, IBrush fallbackBrush) {
        if (Application.Current is null) {
            return fallbackBrush;
        }

        // Try to get the resource from the current application
        ThemeVariant currentTheme = Application.Current.ActualThemeVariant;

        if (Application.Current.TryGetResource(resourceKey, currentTheme, out object? resource) && resource is IBrush brush) {
            return brush;
        }

        return fallbackBrush;
    }
}