namespace Spice86.Views.Converters;

using Avalonia.Data;
using Avalonia.Data.Converters;

using FluentIcons.Common;

using Spice86.ViewModels;

using System;
using System.Globalization;

/// <summary>
/// Converts an <see cref="InstructionCategory"/> to the corresponding
/// <see cref="Symbol"/> for the instruction icon column in the disassembly view.
/// </summary>
internal sealed class InstructionCategoryToSymbolConverter : IValueConverter {
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        if (value is InstructionCategory category) {
            return CategoryToSymbol(category);
        }
        return Symbol.Circle;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
        return BindingOperations.DoNothing;
    }

    private static Symbol CategoryToSymbol(InstructionCategory category) {
        return category switch {
            InstructionCategory.Dos => Symbol.FolderOpen,
            InstructionCategory.Bios => Symbol.Board,
            InstructionCategory.Mouse => Symbol.CursorClick,
            InstructionCategory.Sound => Symbol.MusicNote2,
            InstructionCategory.Video => Symbol.Video,
            InstructionCategory.Memory => Symbol.Database,
            InstructionCategory.Cpu => Symbol.Calculator,
            InstructionCategory.IoPort => Symbol.PlugConnected,
            InstructionCategory.Flow => Symbol.BranchFork,
            _ => Symbol.Circle
        };
    }
}
