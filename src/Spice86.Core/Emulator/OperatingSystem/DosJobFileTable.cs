namespace Spice86.Core.Emulator.OperatingSystem;

using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Utils;

/// <summary>
/// Job file table (JFT) of a process. DOS file handles are indexes in the JFT of the current PSP,
/// and each used JFT entry holds the index of an entry of the system file table (<see cref="DosFileManager.OpenFiles"/>).
/// </summary>
/// <remarks>
/// The table is located through the PSP fields at 32h (size) and 34h (far pointer), so it follows relocations made by INT 21h AH=67h.
/// </remarks>
public class DosJobFileTable {
    /// <summary>
    /// Value of an unused JFT entry.
    /// </summary>
    public const byte UnusedEntry = 0xFF;

    private const ushort MaximumEntries = 0xFF;

    private readonly IMemory _memory;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="memory">The memory bus holding the PSPs and their job file tables.</param>
    public DosJobFileTable(IMemory memory) {
        _memory = memory;
    }

    /// <summary>
    /// Gets the number of handles of the JFT of a process.
    /// </summary>
    /// <param name="pspSegment">The segment of the PSP owning the table.</param>
    public ushort GetSize(ushort pspSegment) {
        ushort size = GetPsp(pspSegment).MaximumOpenFiles;
        return Math.Min(size, MaximumEntries);
    }

    /// <summary>
    /// Gets the raw JFT entry for a handle.
    /// </summary>
    /// <param name="pspSegment">The segment of the PSP owning the table.</param>
    /// <param name="handle">The DOS file handle, which must be lower than <see cref="GetSize"/>.</param>
    /// <returns>The system file table index, or <see cref="UnusedEntry"/>.</returns>
    public byte GetEntry(ushort pspSegment, ushort handle) => _memory.UInt8[GetEntryAddress(pspSegment, handle)];

    /// <summary>
    /// Sets the raw JFT entry for a handle.
    /// </summary>
    /// <param name="pspSegment">The segment of the PSP owning the table.</param>
    /// <param name="handle">The DOS file handle, which must be lower than <see cref="GetSize"/>.</param>
    /// <param name="systemFileTableIndex">The system file table index, or <see cref="UnusedEntry"/>.</param>
    public void SetEntry(ushort pspSegment, ushort handle, byte systemFileTableIndex) {
        _memory.UInt8[GetEntryAddress(pspSegment, handle)] = systemFileTableIndex;
    }

    /// <summary>
    /// Translates a DOS file handle to its system file table index.
    /// </summary>
    /// <param name="pspSegment">The segment of the PSP owning the table.</param>
    /// <param name="handle">The DOS file handle.</param>
    /// <param name="systemFileTableIndex">The system file table index when the handle is in use.</param>
    /// <returns><c>true</c> when the handle is in range and in use.</returns>
    public bool TryResolve(ushort pspSegment, ushort handle, out byte systemFileTableIndex) {
        systemFileTableIndex = UnusedEntry;
        if (handle >= GetSize(pspSegment)) {
            return false;
        }
        systemFileTableIndex = GetEntry(pspSegment, handle);
        return systemFileTableIndex != UnusedEntry;
    }

    /// <summary>
    /// Finds the lowest unused handle of the JFT.
    /// </summary>
    /// <param name="pspSegment">The segment of the PSP owning the table.</param>
    /// <param name="handle">The lowest unused handle when found.</param>
    /// <returns><c>true</c> when an unused handle exists.</returns>
    public bool TryFindFreeHandle(ushort pspSegment, out ushort handle) {
        ushort size = GetSize(pspSegment);
        for (handle = 0; handle < size; handle++) {
            if (GetEntry(pspSegment, handle) == UnusedEntry) {
                return true;
            }
        }
        return false;
    }

    private uint GetEntryAddress(ushort pspSegment, ushort handle) {
        SegmentedAddress tableAddress = GetPsp(pspSegment).FileTableAddress;
        return MemoryUtils.ToPhysicalAddress(tableAddress.Segment, (ushort)(tableAddress.Offset + handle));
    }

    private DosProgramSegmentPrefix GetPsp(ushort pspSegment) => new(_memory, MemoryUtils.ToPhysicalAddress(pspSegment, 0));
}
