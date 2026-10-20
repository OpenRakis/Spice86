namespace Spice86.Tests.Utility;

using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Utils;

/// <summary>Reads text-mode video cells, the behavioral observation of many DOS scenarios.</summary>
internal static class VideoMemoryReader {
    /// <summary>Reads the characters of the first video cells of the text screen.</summary>
    /// <param name="memory">Memory of a finished run.</param>
    /// <param name="length">Number of cells to read.</param>
    /// <returns>The cell characters in display order.</returns>
    public static string ReadCells(IMemory memory, int length) {
        uint videoBase = MemoryUtils.ToPhysicalAddress(0xB800, 0);
        System.Text.StringBuilder output = new(length);
        for (int i = 0; i < length; i++) {
            byte character = memory.UInt8[videoBase + (uint)(i * 2)];
            output.Append((char)character);
        }
        return output.ToString();
    }

    /// <summary>Reads one text-mode video cell as its character and attribute byte.</summary>
    /// <param name="memory">Memory of a finished run.</param>
    /// <param name="row">Screen row of the cell, 0 based.</param>
    /// <param name="column">Screen column of the cell, 0 based.</param>
    /// <returns>The character and the attribute of that cell.</returns>
    public static (char Character, byte Attribute) ReadVideoCell(IMemory memory, int row, int column) {
        uint address = MemoryUtils.ToPhysicalAddress(0xB800, (ushort)((row * 80 + column) * 2));
        return ((char)memory.UInt8[address], memory.UInt8[address + 1]);
    }
}
