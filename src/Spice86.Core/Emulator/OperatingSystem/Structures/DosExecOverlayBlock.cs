namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Memory-backed representation of the EXEC overlay block used by INT 21h AH=4Bh with AL=03h.
/// <code>
/// Layout (ES:BX):
///   +0x00 WORD Load segment address where the image will be loaded.
///   +0x02 WORD Relocation factor to apply to the image.
/// </code>
/// </summary>
public sealed class DosExecOverlayBlock : MemoryBasedDataStructure {
    private const uint OffsetLoadSegment = 0x00;
    private const uint OffsetRelocationFactor = 0x02;

    /// <summary>
    /// Creates a view over the overlay parameter block at ES:BX.
    /// </summary>
    public DosExecOverlayBlock(IByteReaderWriter byteReaderWriter, uint baseAddress)
    : base(byteReaderWriter, baseAddress) {
    }

    /// <summary>
    /// The segment address where the overlay will be loaded.
    /// </summary>
    public ushort LoadSegment {
        get => UInt16[OffsetLoadSegment];
        set => UInt16[OffsetLoadSegment] = value;
    }

    /// <summary>
    /// The relocation factor to be applied to the overlay image.
    /// </summary>
    public ushort RelocationFactor {
        get => UInt16[OffsetRelocationFactor];
        set => UInt16[OffsetRelocationFactor] = value;
    }
}
