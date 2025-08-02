namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using System;

/// <summary>
/// Centralizes global DOS memory structures
/// </summary>
public class DosTables {
    public const ushort DosPrivateTablesSegmentStart = 0xC800;
    public const ushort DosPrivateTablesSegmentEnd = 0xD000;

    public ushort CurrentMemorySegment { get; set; } = DosPrivateTablesSegmentStart;

    /// <summary>
    /// The current country information
    /// </summary>
    public CountryInfo CountryInfo { get; set; } = new();

    public ushort GetDosPrivateTableWritableAddress(ushort pages) {
        if (pages + CurrentMemorySegment >= DosPrivateTablesSegmentEnd) {
            throw new InvalidOperationException("DOS: Not enough memory for internal tables!");
        }
        ushort page = CurrentMemorySegment;
        CurrentMemorySegment += pages;
        return page;
    }
}