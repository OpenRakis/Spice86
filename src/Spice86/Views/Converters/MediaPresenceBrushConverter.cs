namespace Spice86.Views.Converters;

using Avalonia.Data.Converters;
using Avalonia.Media;

using System;
using System.Globalization;

/// <summary>
/// Converts a <see cref="bool"/> media-present value to a background brush:
/// green-ish when media is present, grey when empty/ejected.
/// </summary>
public sealed class MediaPresenceBrushConverter : IValueConverter {
    /// <summary>The brush used when media (disk, CD, etc.) is present in the drive.</summary>
    private static readonly IBrush MediaPresentBrush = new SolidColorBrush(Color.FromRgb(0x20, 0x80, 0x30), 0.25);

    /// <summary>The brush used when no media is present.</summary>
    private static readonly IBrush NoMediaBrush = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80), 0.15);

    /// <inheritdoc/>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        if (value is bool hasMedia && hasMedia) {
            return MediaPresentBrush;
        }
        return NoMediaBrush;
    }

    /// <inheritdoc/>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
        throw new NotSupportedException($"{nameof(MediaPresenceBrushConverter)} does not support ConvertBack.");
    }
}
