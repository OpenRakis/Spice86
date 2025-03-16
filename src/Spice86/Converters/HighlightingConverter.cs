namespace Spice86.Converters;

using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;

/// <summary>
/// Provides converters for highlighting the current instruction in the disassembly view.
/// </summary>
public static class HighlightingConverter
{
    // Static resource keys
    private const string HighlightBackgroundKey = "DisassemblyLineHighlightBrush";
    private const string HighlightForegroundKey = "DisassemblyLineHighlightForegroundBrush";

    // Default brushes to use if resource lookup fails
    private static readonly IBrush DefaultHighlightBackground = new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x50));
    private static readonly IBrush DefaultHighlightForeground = new SolidColorBrush(Color.FromRgb(0x1E, 0x90, 0xFF));

    /// <summary>
    /// Converts a boolean to a brush for background highlighting.
    /// Uses a dynamic resource when true, transparent brush when false.
    /// </summary>
    public static readonly IValueConverter TrueToHighlightBackground = new FuncValueConverter<bool, IBrush>(
        value => value ? GetResourceBrush(HighlightBackgroundKey, DefaultHighlightBackground) : Brushes.Transparent);

    /// <summary>
    /// Converts a boolean to a brush for foreground highlighting.
    /// Uses a dynamic resource when true, transparent brush when false to use the default foreground.
    /// </summary>
    public static readonly IValueConverter TrueToHighlightForeground = new FuncValueConverter<bool, IBrush>(
        value => value ? GetResourceBrush(HighlightForegroundKey, DefaultHighlightForeground) : Brushes.Transparent);

    /// <summary>
    /// Converts a boolean to a FontWeight (true = Bold, false = Normal).
    /// </summary>
    public static readonly IValueConverter TrueToBold = new FuncValueConverter<bool, FontWeight>(
        value => value ? FontWeight.Bold : FontWeight.Normal);
        
    /// <summary>
    /// Gets a brush from application resources with a fallback value if not found.
    /// </summary>
    /// <param name="resourceKey">The resource key to look up.</param>
    /// <param name="defaultBrush">The default brush to use if the resource is not found.</param>
    /// <returns>The brush from resources or the default brush.</returns>
    private static IBrush GetResourceBrush(string resourceKey, IBrush defaultBrush)
    {
        if (Application.Current == null)
        {
            return defaultBrush;
        }

        // Try to get the resource from the current application
        // Get the current theme variant (Light/Dark)
        ThemeVariant currentTheme = Application.Current.ActualThemeVariant;
        
        if (Application.Current.TryGetResource(resourceKey, currentTheme, out object? resource) && resource is IBrush brush)
        {
            return brush;
        }

        return defaultBrush;
    }
}
