namespace Spice86.Views.Converters;

using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;

using Spice86.ViewModels;

using System;
using System.Collections.Generic;
using System.Globalization;

/// <summary>
/// Converts an <see cref="InstructionCategory"/> to a custom vector <see cref="StreamGeometry"/>
/// for the instruction icon column in the disassembly view.
/// </summary>
/// <remarks>
/// Each category maps to a small custom path designed to be distinctive and meaningful
/// for the DOS/BIOS/hardware debugging context (e.g. floppy disk for DOS, IC chip for BIOS,
/// speaker for Sound, CRT monitor for Video, RAM module for Memory, processor die for CPU,
/// D-sub connector for I/O port, decision diamond for control flow).
/// </remarks>
internal sealed class InstructionCategoryToGeometryConverter : IValueConverter {
    private static readonly Dictionary<InstructionCategory, Geometry> _geometries = BuildGeometries();

    private static Dictionary<InstructionCategory, Geometry> BuildGeometries() {
        Dictionary<InstructionCategory, string> paths = new() {
            // DOS — floppy disk: rectangular body + label area + disk window
            [InstructionCategory.Dos] =
                "M 1,1 L 13,1 L 13,13 L 1,13 Z " +
                "M 2,2 L 8,2 L 8,5 L 2,5 Z " +
                "M 3,7 L 11,7 L 11,12 L 3,12 Z",

            // BIOS — IC chip: rectangle body + 2 pins each side
            [InstructionCategory.Bios] =
                "M 4,2 L 10,2 L 10,12 L 4,12 Z " +
                "M 1,5 L 4,5 M 1,9 L 4,9 M 10,5 L 13,5 M 10,9 L 13,9",

            // Mouse — pointer cursor arrow
            [InstructionCategory.Mouse] =
                "M 2,1 L 2,11 L 5,8 L 7,13 L 9,12 L 7,7 L 11,7 Z",

            // Sound — speaker cone with two sound waves
            [InstructionCategory.Sound] =
                "M 1,5 L 1,9 L 4,9 L 7,12 L 7,2 L 4,5 Z " +
                "M 9,4 Q 11,7 9,10 " +
                "M 10.5,2.5 Q 13.5,7 10.5,11.5",

            // Video — CRT monitor: screen + pedestal + base
            [InstructionCategory.Video] =
                "M 1,1 L 13,1 L 13,9 L 1,9 Z " +
                "M 5,9 L 9,9 L 9,13 L 5,13 Z " +
                "M 3,13 L 11,13",

            // Memory — RAM stick: body + contact pins at top and bottom
            [InstructionCategory.Memory] =
                "M 1,4 L 13,4 L 13,10 L 1,10 Z " +
                "M 3,2 L 3,4 M 5,2 L 5,4 M 7,2 L 7,4 M 9,2 L 9,4 M 11,2 L 11,4 " +
                "M 3,10 L 3,12 M 5,10 L 5,12 M 7,10 L 7,12 M 9,10 L 9,12 M 11,10 L 11,12",

            // CPU — processor die: square die + 3 pins each side
            [InstructionCategory.Cpu] =
                "M 4,4 L 10,4 L 10,10 L 4,10 Z " +
                "M 5,2 L 5,4 M 7,2 L 7,4 M 9,2 L 9,4 " +
                "M 5,10 L 5,12 M 7,10 L 7,12 M 9,10 L 9,12 " +
                "M 2,5 L 4,5 M 2,7 L 4,7 M 2,9 L 4,9 " +
                "M 10,5 L 12,5 M 10,7 L 12,7 M 10,9 L 12,9",

            // IoPort — D-sub connector housing + 3 contact pins
            [InstructionCategory.IoPort] =
                "M 1,4 L 13,4 L 13,10 L 1,10 Z " +
                "M 3,6 L 3,8 M 7,5 L 7,9 M 11,6 L 11,8",

            // Flow — decision diamond
            [InstructionCategory.Flow] =
                "M 7,1 L 13,7 L 7,13 L 1,7 Z",
        };

        Dictionary<InstructionCategory, Geometry> result = new(paths.Count);
        foreach (KeyValuePair<InstructionCategory, string> entry in paths) {
            result[entry.Key] = StreamGeometry.Parse(entry.Value);
        }
        return result;
    }

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        if (value is InstructionCategory category && _geometries.TryGetValue(category, out Geometry? geometry)) {
            return geometry;
        }
        return null;
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
        return BindingOperations.DoNothing;
    }
}
