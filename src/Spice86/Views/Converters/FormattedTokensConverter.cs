namespace Spice86.Views.Converters;

using Avalonia.Controls.Documents;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Threading;

using Spice86.ViewModels.TextPresentation;

using System;
using System.Collections.Generic;
using System.Globalization;

/// <summary>
/// Converts a list of <see cref="FormattedToken"/> to an <see cref="InlineCollection"/> for display in a TextBlock.
/// </summary>
public class FormattedTokensConverter : IValueConverter {
    /// <summary>
    /// Converts a list of <see cref="FormattedToken"/> to an <see cref="InlineCollection"/>.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    /// <param name="targetType">The type of the target.</param>
    /// <param name="parameter">The converter parameter.</param>
    /// <param name="culture">The culture to use.</param>
    /// <returns>An InlineCollection containing Run objects for each token.</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        if (value is not List<FormattedToken> tokens) {
            return null;
        }

        // Create the InlineCollection on the UI thread
        if (!Dispatcher.UIThread.CheckAccess()) {
            // If we're not on the UI thread, return a new empty collection
            // The actual conversion will happen when the UI thread processes the binding
            return new InlineCollection();
        }

        InlineCollection inlines = new InlineCollection();

        foreach (FormattedToken token in tokens) {
            Run run = new Run {
                Text = token.Text,
            };
            run.Bind(TextElement.ForegroundProperty, FormatterTextKindToBrushConverter.GetDynamicResourceExtension(token.Kind));
            inlines.Add(run);
        }

        return inlines;
    }

    /// <summary>
    /// Not implemented.
    /// </summary>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
        return BindingOperations.DoNothing;
    }
}
