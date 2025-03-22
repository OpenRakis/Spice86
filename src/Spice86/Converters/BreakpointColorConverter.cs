namespace Spice86.Converters;

using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;

using Spice86.ViewModels;

using System;
using System.Globalization;

internal class BreakpointColorConverter : IValueConverter {
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        if (value is BreakpointViewModel breakpoint) {
            return breakpoint.IsEnabled
                ? ConverterUtilities.GetResourceBrush("DisassemblyBreakpointEnabledBrush", Brushes.Red)
                : ConverterUtilities.GetResourceBrush("DisassemblyBreakpointDisabledBrush", Brushes.Gray);
        }

        return Brushes.Transparent;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
        return BindingOperations.DoNothing;
    }
}