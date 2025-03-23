namespace Spice86.Converters;

using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;

/// <summary>
/// Provides converters for highlighting the current instruction in the disassembly view.
/// </summary>
public static class HighlightingConverter {
    // Static resource keys
    private const string DefaultBackgroundKey = "WindowDefaultBackground";
    private const string HighlightBackgroundKey = "DisassemblyLineHighlightBrush";
    private const string DefaultForegroundKey = "WindowDefaultForeground";
    private const string HighlightForegroundKey = "DisassemblyLineHighlightForegroundBrush";

    // Default brushes to use if resource lookup fails
    private static readonly IBrush FallbackDefaultBackground = Brushes.Transparent;
    private static readonly IBrush FallbackHighlightBackground = new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x50));
    private static readonly IBrush FallbackDefaultForeground = Brushes.White;
    private static readonly IBrush FallbackHighlightForeground = new SolidColorBrush(Color.FromRgb(0x1E, 0x90, 0xFF));

    /// <summary>
    /// Converts a boolean to a brush for background highlighting.
    /// Uses a dynamic resource when true, transparent brush when false.
    /// </summary>
    public static readonly IValueConverter TrueToHighlightBackground = new FuncValueConverter<bool, IBrush>(value => value ? GetHighlightBackgroundBrush() : GetDefaultBackgroundBrush());

    /// <summary>
    /// Converts a boolean to a brush for foreground highlighting.
    /// Uses a dynamic resource when true, transparent brush when false to use the default foreground.
    /// </summary>
    public static readonly IValueConverter TrueToHighlightForeground = new FuncValueConverter<bool, IBrush>(value => value ? GetHighlightForegroundBrush() : GetDefaultForegroundBrush());

    /// <summary>
    /// Converts a boolean to a FontWeight (true = Bold, false = Normal).
    /// </summary>
    public static readonly IValueConverter TrueToBold = new FuncValueConverter<bool, FontWeight>(value => value ? FontWeight.Bold : FontWeight.Normal);

    /// <summary>
    /// Gets the highlight background brush used by the converters.
    /// </summary>
    public static IBrush GetHighlightBackgroundBrush() {
        return ConverterUtilities.GetResourceBrush(HighlightBackgroundKey, FallbackHighlightBackground);
    }

    /// <summary>
    /// Gets the default background brush used by the converters.
    /// </summary>
    public static IBrush GetDefaultBackgroundBrush() {
        return ConverterUtilities.GetResourceBrush(DefaultBackgroundKey, FallbackDefaultBackground);
    }

    /// <summary>
    /// Gets the highlight foreground brush used by the converters.
    /// </summary>
    public static IBrush GetHighlightForegroundBrush() {
        return ConverterUtilities.GetResourceBrush(HighlightForegroundKey, FallbackHighlightForeground);
    }

    /// <summary>
    /// Gets the default foreground brush used by the converters.
    /// </summary>
    public static IBrush GetDefaultForegroundBrush() {
        return ConverterUtilities.GetResourceBrush(DefaultForegroundKey, FallbackDefaultForeground);
    }
}