namespace Spice86.Views.Converters;

using Avalonia.Data.Converters;

using System.Globalization;

/// <summary>Converts a boolean "loaded" flag to "Loaded"/"Not loaded".</summary>
public sealed class LoadedConverter : IValueConverter {
    /// <summary>Shared instance.</summary>
    public static readonly LoadedConverter Instance = new();

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        if (value is bool b && b) {
            return "Loaded";
        }
        return "Not loaded";
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
        throw new NotSupportedException();
    }
}
