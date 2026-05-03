namespace Spice86.Views.Converters;

using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;

using Spice86.ViewModels;

using System;
using System.Collections.Generic;
using System.Globalization;

/// <summary>
/// Converts an <see cref="InstructionCategory"/> to a distinct <see cref="IBrush"/>
/// used as both the fill and stroke colour for the custom icon in the disassembly view.
/// </summary>
internal sealed class InstructionCategoryToFillConverter : IValueConverter {
    private static readonly Dictionary<InstructionCategory, IBrush> _brushes = new() {
        [InstructionCategory.Dos]    = new SolidColorBrush(Color.Parse("#2E86C1")),  // DOS blue
        [InstructionCategory.Bios]   = new SolidColorBrush(Color.Parse("#28B463")),  // BIOS green
        [InstructionCategory.Mouse]  = new SolidColorBrush(Color.Parse("#8E44AD")),  // purple
        [InstructionCategory.Sound]  = new SolidColorBrush(Color.Parse("#D68910")),  // amber/orange
        [InstructionCategory.Video]  = new SolidColorBrush(Color.Parse("#1ABC9C")),  // teal
        [InstructionCategory.Memory] = new SolidColorBrush(Color.Parse("#CB4335")),  // red
        [InstructionCategory.Cpu]    = new SolidColorBrush(Color.Parse("#707070")),  // gray
        [InstructionCategory.IoPort] = new SolidColorBrush(Color.Parse("#5B2C6F")),  // indigo/dark purple
        [InstructionCategory.Flow]   = new SolidColorBrush(Color.Parse("#A04000")),  // rust/brown
    };

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        if (value is InstructionCategory category && _brushes.TryGetValue(category, out IBrush? brush)) {
            return brush;
        }
        return Brushes.Transparent;
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
        return BindingOperations.DoNothing;
    }
}
