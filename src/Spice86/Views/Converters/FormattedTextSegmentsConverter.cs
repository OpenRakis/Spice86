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
/// Converts a list of <see cref="FormattedTextOffset"/> to an <see cref="InlineCollection"/>.
/// </summary>
public class FormattedTextOffsetsConverter : IValueConverter {
    /// <summary>
    /// Converts a list of <see cref="FormattedTextOffset"/> to an <see cref="InlineCollection"/>.
    /// </summary>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        if (value is not List<FormattedTextOffset> textOffsets) {
            return null;
        }

        if (!Dispatcher.UIThread.CheckAccess()) {
            return new InlineCollection();
        }

        InlineCollection inlines = new();

        foreach (FormattedTextOffset textOffset in textOffsets) {
            Run run = new() {
                Text = textOffset.Text,
            };
            run.Bind(TextElement.ForegroundProperty,
                FormatterTextKindToBrushConverter.GetDynamicResourceExtension(textOffset.Kind));
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