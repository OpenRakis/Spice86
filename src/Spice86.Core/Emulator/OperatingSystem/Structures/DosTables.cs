namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Utils;

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

    /// <summary>
    /// Gets the Current Directory Structure (CDS) for DOS drives.
    /// </summary>
    public CurrentDirectoryStructure CurrentDirectoryStructure { get; private set; } = default!;

    /// <summary>
    /// Gets the Double Byte Character Set (DBCS) lead-byte table.
    /// </summary>
    public DosDoubleByteCharacterSet DoubleByteCharacterSet { get; private set; } = default!;

    /// <summary>
    /// Initializes the DOS table structures in memory.
    /// </summary>
    /// <param name="memory">The memory interface to write structures to.</param>
    public void Initialize(IByteReaderWriter memory) {
        uint cdsAddress = MemoryUtils.ToPhysicalAddress(MemoryMap.DosCdsSegment, 0);
        CurrentDirectoryStructure = new CurrentDirectoryStructure(memory, cdsAddress);

        ushort currentMemorySegment = ReserveDosPrivateSegment(DosDoubleByteCharacterSet.DbcsTableSizeInParagraphs);
        ushort doubleByteCharacterSetSegment = (ushort)(currentMemorySegment + 1);
        uint dbcsAddress = MemoryUtils.ToPhysicalAddress(doubleByteCharacterSetSegment, 0);
        DoubleByteCharacterSet = new DosDoubleByteCharacterSet(memory, dbcsAddress);
    }

    /// <summary>
    /// Reserves memory in the DOS private tables segment area (0xC800-0xD000).
    /// </summary>
    /// <param name="paragraphs">Number of paragraphs (16-byte blocks) to allocate.</param>
    /// <returns>The current segment address pointer in the DOS private tables area, before the reservation is made.</returns>
    /// <exception cref="InvalidOperationException">Thrown when there is insufficient memory in the DOS private tables area.</exception>
    public ushort ReserveDosPrivateSegment(ushort paragraphs) {
        if (paragraphs + CurrentMemorySegment >= DosPrivateTablesSegmentEnd) {
            throw new InvalidOperationException("DOS: Not enough memory for internal tables!");
        }
        ushort segmentNumber = CurrentMemorySegment;
        CurrentMemorySegment += paragraphs;
        return segmentNumber;
    }
}