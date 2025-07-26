namespace Spice86.Views.Converters;

using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;

using Spice86.ViewModels;

using System;
using System.Globalization;

internal class BreakpointColorConverter : IValueConverter {
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        const string disassemblybreakpointenabledbrush = "DisassemblyBreakpointEnabledBrush";
        const string disassemblybreakpointdisabledbrush = "DisassemblyBreakpointDisabledBrush";

        if (value is bool isEnabled) {
            return isEnabled
                ? ConverterUtilities.GetResourceBrush(disassemblybreakpointenabledbrush, Brushes.Red)
                : ConverterUtilities.GetResourceBrush(disassemblybreakpointdisabledbrush, Brushes.Gray);
        }

        if (value is BreakpointViewModel breakpoint) {
            return breakpoint.IsEnabled
                ? ConverterUtilities.GetResourceBrush(disassemblybreakpointenabledbrush, Brushes.Red)
                : ConverterUtilities.GetResourceBrush(disassemblybreakpointdisabledbrush, Brushes.Gray);
        }

        return Brushes.Transparent;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
        return BindingOperations.DoNothing;
    }
}