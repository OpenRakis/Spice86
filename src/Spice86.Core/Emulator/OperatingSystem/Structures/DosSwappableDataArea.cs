namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure;

/// <summary>
/// Represents a DOS SDA (Swappable Data Area) in emulated memory.
/// Real implementation of MS-DOS SDA has way more fields than this, but DOSBox doesn't emulate it, so we don't either.
/// </summary>
public sealed class DosSwappableDataArea : MemoryBasedDataStructure {
    public DosSwappableDataArea(IByteReaderWriter byteReaderWriter, uint baseAddress) : base(byteReaderWriter, baseAddress) {
    }

    /// <summary>
    /// Gets or sets the DOS critical error flag.
    /// </summary>
    public byte CriticalErrorFlag { get => UInt8[0x0]; set => UInt8[0x0] = value; }

    /// <summary>
    /// Gets or sets the INDOS flag (count of active INT 0x21 calls).
    /// </summary>
    public byte InDosFlag { get => UInt8[0x1]; set => UInt8[0x1] = value; }

    /// <summary>
    /// Gets or sets the drive on which the current critical error occurred or 0xFFh.
    /// </summary>
    public byte DriveCriticalError {
        get => UInt8[0x2];
        set => UInt8[0x2] = value;
    }

    /// <summary>
    /// Gets or sets the locus of the last error.
    /// </summary>
    public byte LocusOfLastError {
        get => UInt8[0x3];
        set => UInt8[0x3] = value;
    }

    /// <summary>
    /// Gets or sets the extended error code of the last error.
    /// </summary>
    public ushort ExtendedErrorCode {
        get => UInt16[0x4];
        set => UInt16[0x4] = value;
    }

    /// <summary>
    /// Gets or sets the suggested action for the last error.
    /// </summary>
    public byte SuggestedAction {
        get => UInt8[0x6];
        set => UInt8[0x6] = value;
    }

    /// <summary>
    /// Gets or sets the class of the last error.
    /// </summary>
    public byte ErrorClass {
        get => UInt8[0x7];
        set => UInt8[0x7] = value;
    }

    /// <summary>
    /// Gets or sets the ES:DI pointer for the last error.
    /// </summary>
    public uint LastErrorPointer {
        get => UInt32[0x8];
        set => UInt32[0x8] = value;
    }

    /// <summary>
    /// Gets or sets the current DTA (Disk Transfer Area).
    /// </summary>
    public uint CurrentDiskTransferArea {
        get => UInt32[0xC];
        set => UInt32[0xC] = value;
    }

    /// <summary>
    /// Gets or sets the current PSP (Program Segment Prefix).
    /// </summary>
    public ushort CurrentProgramSegmentPrefix {
        get => UInt16[0x10];
        set => UInt16[0x10] = value;
    }

    /// <summary>
    /// Gets or sets the stored value of the SP register. Used by INT 0x23.
    /// </summary>
    public ushort SpInt23 {
        get => UInt16[0x12];
        set => UInt16[0x12] = value;
    }

    /// <summary>
    /// Gets or sets the return code from the last process termination.
    /// </summary>
    public ushort ReturnCode {
        get => UInt16[0x14];
        set => UInt16[0x14] = value;
    }

    /// <summary>
    /// Gets or sets the current drive. 0x0: A:, 0x1: B:, etc.
    /// </summary>
    public byte CurrentDrive { get => UInt8[0x16]; set => UInt8[0x16] = value; }

    /// <summary>
    /// Gets or sets the keyboard extended break flag.
    /// </summary>
    public byte ExtendedBreakFlag { get => UInt8[0x17]; set => UInt8[0x17] = value; }

    /// <summary>
    /// Gets or sets the flag for code page switching. Unused since we don't support code pages.
    /// </summary>
    public byte CodePageSwitchingFlag { get => UInt8[0x18]; set => UInt8[0x18] = value; }

    /// <summary>
    /// Gets or sets the copy of the previous byte. MS-DOS uses it for DOS INT 0x28 Abort call. Unused in our implementation.
    /// </summary>
    public byte PreviousByteInt28 { get => UInt8[0x19]; set => UInt8[0x19] = value; }
}
