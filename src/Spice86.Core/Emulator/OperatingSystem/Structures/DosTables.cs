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
    public CurrentDirectoryStructure? CurrentDirectoryStructure { get; private set; }

    /// <summary>
    /// Gets the Double Byte Character Set (DBCS) lead-byte table.
    /// </summary>
    public DosDoubleByteCharacterSet? DoubleByteCharacterSet { get; private set; }

    /// <summary>
    /// Initializes the DOS table structures in memory.
    /// This method allocates and initializes the CDS (Current Directory Structure)
    /// and DBCS (Double Byte Character Set) tables at their designated memory locations.
    /// </summary>
    /// <param name="memory">The memory interface to write structures to.</param>
    public void Initialize(IByteReaderWriter memory) {
        // Allocate CDS at fixed segment (MemoryMap.DosCdsSegment = 0x108)
        uint cdsAddress = MemoryUtils.ToPhysicalAddress(MemoryMap.DosCdsSegment, 0);
        CurrentDirectoryStructure = new CurrentDirectoryStructure(memory, cdsAddress);

        // Allocate DBCS table in DOS private tables area (0xC800-0xD000)
        // Allocate 12 paragraphs (192 bytes) to match DOSBox's DOS_GetMemory(12)
        ushort dbcsSegment = GetDosPrivateTableWritableAddress(12);
        uint dbcsAddress = MemoryUtils.ToPhysicalAddress(dbcsSegment, 0);
        DoubleByteCharacterSet = new DosDoubleByteCharacterSet(memory, dbcsAddress);
    }

    /// <summary>
    /// Allocates memory in the DOS private tables segment area (0xC800-0xD000).
    /// </summary>
    /// <param name="pages">Number of paragraphs (16-byte blocks) to allocate.</param>
    /// <returns>The segment address of the allocated memory.</returns>
    /// <exception cref="InvalidOperationException">Thrown when there is insufficient memory in the DOS private tables area.</exception>
    public ushort GetDosPrivateTableWritableAddress(ushort pages) {
        if (pages + CurrentMemorySegment >= DosPrivateTablesSegmentEnd) {
            throw new InvalidOperationException("DOS: Not enough memory for internal tables!");
        }
        ushort page = CurrentMemorySegment;
        CurrentMemorySegment += pages;
        return page;
    }
}