namespace Spice86.Converters;

using Avalonia.Controls.Documents;
using Avalonia.Data.Converters;
using Avalonia.Threading;

using Spice86.ViewModels;

using System;
using System.Collections.Generic;
using System.Globalization;

/// <summary>
/// Converts a list of FormattedTextSegment to an InlineCollection for display in a TextBlock.
/// </summary>
public class FormattedTextSegmentsConverter : IValueConverter {
    /// <summary>
    /// Converts a list of FormattedTextSegment to an InlineCollection.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    /// <param name="targetType">The type of the target.</param>
    /// <param name="parameter">The converter parameter.</param>
    /// <param name="culture">The culture to use.</param>
    /// <returns>An InlineCollection containing Run objects for each segment.</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        if (value is not List<FormattedTextSegment> segments) {
            return null;
        }

        // Create the InlineCollection on the UI thread
        if (!Dispatcher.UIThread.CheckAccess()) {
            // If we're not on the UI thread, return a new empty collection
            // The actual conversion will happen when the UI thread processes the binding
            return new InlineCollection();
        }

        var inlines = new InlineCollection();

        foreach (FormattedTextSegment segment in segments) {
            var run = new Run {
                Text = segment.Text,
            };
            run.Bind(TextElement.ForegroundProperty, FormatterTextKindToBrushConverter.GetDynamicResourceExtension(segment.Kind));
            inlines.Add(run);
        }

        return inlines;
    }

    /// <summary>
    /// Not implemented.
    /// </summary>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
        throw new NotImplementedException();
    }
}