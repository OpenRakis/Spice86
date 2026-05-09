namespace Spice86.Views.Converters;

using Avalonia.Data;
using Avalonia.Data.Converters;

using FluentIcons.Common;

using Spice86.Shared.Emulator.VM.Breakpoint;

using System;
using System.Globalization;

/// <summary>
/// Maps a <see cref="BreakPointType"/> to a <see cref="Symbol"/> used by
/// <c>FluentIcons.Avalonia.SymbolIcon</c> in the breakpoints data grid.
/// The icons inherit the data grid's foreground brush, so they render
/// correctly under both light and dark themes.
/// </summary>
internal class BreakPointTypeToSymbolConverter : IValueConverter {
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        if (value is not BreakPointType type) {
            return Symbol.Bookmark;
        }
        return type switch {
            BreakPointType.CPU_EXECUTION_ADDRESS => Symbol.Run,
            BreakPointType.CPU_INTERRUPT => Symbol.Flash,
            BreakPointType.CPU_CYCLES => Symbol.Timer,
            BreakPointType.MEMORY_READ => Symbol.Glasses,
            BreakPointType.MEMORY_WRITE => Symbol.Compose,
            BreakPointType.MEMORY_ACCESS => Symbol.Storage,
            BreakPointType.IO_READ => Symbol.ArrowImport,
            BreakPointType.IO_WRITE => Symbol.ArrowExport,
            BreakPointType.IO_ACCESS => Symbol.PlugConnectedSettings,
            BreakPointType.MACHINE_START => Symbol.PlayCircle,
            BreakPointType.MACHINE_STOP => Symbol.Stop,
            _ => Symbol.Bookmark,
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
        return BindingOperations.DoNothing;
    }
}
