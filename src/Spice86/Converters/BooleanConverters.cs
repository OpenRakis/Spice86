namespace Spice86.Converters;

using Avalonia.Data.Converters;
using Avalonia.Media;

/// <summary>
/// Provides static boolean converters for common UI transformations.
/// </summary>
public static class BooleanConverters {
    // Static brushes to avoid creating new brushes on each conversion
    private static readonly IBrush HighlightBackground = new SolidColorBrush(Color.Parse("#1E90FF20"));
    private static readonly IBrush HighlightForeground = new SolidColorBrush(Color.Parse("#1E90FF"));
    
    /// <summary>
    /// Converts a boolean to a FontWeight (true = Bold, false = Normal).
    /// </summary>
    public static readonly IValueConverter TrueToBold = new FuncValueConverter<bool, FontWeight>(
        value => value ? FontWeight.Bold : FontWeight.Normal);
        
    /// <summary>
    /// Converts a boolean to a background brush (true = highlight, false = transparent).
    /// </summary>
    public static readonly IValueConverter TrueToHighlightBackground = new FuncValueConverter<bool, IBrush>(
        value => value ? HighlightBackground : Brushes.Transparent);
        
    /// <summary>
    /// Converts a boolean to a foreground brush (true = highlight color, false = default text).
    /// </summary>
    public static readonly IValueConverter TrueToHighlightForeground = new FuncValueConverter<bool, IBrush>(
        value => value ? HighlightForeground : Brushes.Teal);
}
