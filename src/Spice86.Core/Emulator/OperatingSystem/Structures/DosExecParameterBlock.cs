namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Memory-backed representation of the EXEC parameter block used by INT 21h AH=4Bh for functions 00h and 01h.
/// Layout (ES:BX):
/// <code>
///   +0x00 WORD  Environment segment (0 means inherit parent's).
///   +0x02 DWORD Far pointer to command tail (typically parent's PSP:80h).
///   +0x06 DWORD Far pointer to default FCB at 5Ch.
///   +0x0A DWORD Far pointer to default FCB at 6Ch.
///   [Func 01h only - returned by DOS]
///   +0x0E DWORD Returned CS:IP entry point of loaded program.
///   +0x12 DWORD Returned SS:SP initial stack of loaded program.
/// </code>
/// </summary>
/// <remarks>
/// This structure only describes the parameter block; the executable pathname is passed separately at DS:DX.
/// Use <see cref="DosExecOverlayBlock"/> for function 03h (overlay).
/// </remarks>
public sealed class DosExecParameterBlock : MemoryBasedDataStructure {
    // Offsets within the block (ES:BX)
    private const uint OffsetEnvironmentSegment = 0x00;
    private const uint OffsetCmdTailPtr = 0x02;
    private const uint OffsetFcb1Ptr = 0x06;
    private const uint OffsetFcb2Ptr = 0x0A;
    private const uint OffsetRetCSIP = 0x0E; // only for AL=01h
    private const uint OffsetRetSSSP = 0x12; // only for AL=01h

    /// <summary>
    /// Creates a view over the EXEC parameter block at ES:BX.
    /// </summary>
    public DosExecParameterBlock(IByteReaderWriter byteReaderWriter, uint baseAddress)
    : base(byteReaderWriter, baseAddress) {
    }

    /// <summary>
    /// WORD environment segment. 0 means inherit the parent's environment.
    /// </summary>
    public ushort EnvironmentSegment {
        get => UInt16[OffsetEnvironmentSegment];
        set => UInt16[OffsetEnvironmentSegment] = value;
    }

    /// <summary>
    /// Far pointer to the command tail (usually PSP:80h). The command tail is the DOS-style
    /// count byte followed by characters ending with a carriage return (0x0D).
    /// </summary>
    public SegmentedAddress CommandTailPointer {
        get => SegmentedAddress16[OffsetCmdTailPtr];
        set => SegmentedAddress16[OffsetCmdTailPtr] = value;
    }

    /// <summary>
    /// Far pointer to the default FCB at offset 5Ch in the parent's PSP.
    /// </summary>
    public SegmentedAddress Fcb1Pointer {
        get => SegmentedAddress16[OffsetFcb1Ptr];
        set => SegmentedAddress16[OffsetFcb1Ptr] = value;
    }

    /// <summary>
    /// Far pointer to the default FCB at offset 6Ch in the parent's PSP.
    /// </summary>
    public SegmentedAddress Fcb2Pointer {
        get => SegmentedAddress16[OffsetFcb2Ptr];
        set => SegmentedAddress16[OffsetFcb2Ptr] = value;
    }

    /// <summary>
    /// Returned entry point CS:IP when AL=01h (Load but do not execute).
    /// </summary>
    public SegmentedAddress ReturnedEntryPoint {
        get => SegmentedAddress16[OffsetRetCSIP];
        set => SegmentedAddress16[OffsetRetCSIP] = value;
    }

    /// <summary>
    /// Returned initial stack SS:SP when AL=01h (Load but do not execute).
    /// </summary>
    public SegmentedAddress ReturnedInitialStack {
        get => SegmentedAddress16[OffsetRetSSSP];
        set => SegmentedAddress16[OffsetRetSSSP] = value;
    }
}
