namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure.Array;
using Spice86.Shared.Emulator.Memory;

using System.Diagnostics;

/// <summary>
/// Represents the Program Segment Prefix (PSP)
/// </summary>
[DebuggerDisplay("BaseAddress={BaseAddress}, Parent={ParentProgramSegmentPrefix}, EnvSegment={EnvironmentTableSegment}, NextSegment={NextSegment}, StackPointer={StackPointer}, Cmd={DosCommandTail.Command}")]
public sealed class DosProgramSegmentPrefix : MemoryBasedDataStructure {
    /// <summary>
    /// Full PSP size in bytes, including the command tail: 0x100 (256) bytes total, where
    /// 0x00-0x7F contain PSP structures and 0x80-0xFF are the 128-byte command tail buffer (count + data).
    /// </summary>
    public const ushort MaxLength = 0x100;
    /// <summary>
    /// The size of the PSP struct. Important for program loading.
    /// </summary>
    public const ushort PspSize = 0x100;
    /// <summary>
    /// Specifies the size, in DOS paragraphs, of a Program Segment Prefix (PSP).
    /// </summary>
    public const ushort PspSizeInParagraphs = 0x10;

    public DosProgramSegmentPrefix(IByteReaderWriter byteReaderWriter, uint baseAddress) : base(byteReaderWriter, baseAddress) {
    }

    /// <summary>
    /// CP/M like exit point for INT 0x20. (machine code: 0xCD, 0x20). Old way to exit the program.
    /// </summary>
    public UInt8Array Exit => GetUInt8Array(0x0, 2);

    /// <summary>
    /// Size of the Program. This is used to guess the size of conventional memory (Dune does this).
    /// </summary>
    /// <remarks>
    /// Specified in paragraphs. Create Child PSP has it as a parameter in BX register.
    /// </remarks>
    public ushort CurrentSize { get => UInt16[0x2]; set => UInt16[0x2] = value; }

    /// <summary>
    /// Reserved
    /// </summary>
    public byte Reserved { get => UInt8[0x4]; set => UInt8[0x4] = value; }

    /// <summary>
    /// Far call to DOS INT 0x21 dispatcher. Obsolete.
    /// </summary>
    public byte FarCall { get => UInt8[0x5]; set => UInt8[0x5] = value; }

    public SegmentedAddress CpmServiceRequestAddress { get => SegmentedAddress16[0x6]; set => SegmentedAddress16[0x6] = value; }

    /// <summary>
    /// On exit, DOS copies this to the INT 0x22 vector.
    /// </summary>
    public SegmentedAddress TerminateAddress { get => SegmentedAddress16[0x0A]; set => SegmentedAddress16[0x0A] = value; }

    /// <summary>
    /// On exit, DOS copies this to the INT 0x23 vector.
    /// </summary>
    public SegmentedAddress BreakAddress { get => SegmentedAddress16[0x0E]; set => SegmentedAddress16[0x0E] = value; }

    /// <summary>
    /// On exit, DOS copies this to the INT 0x24 vector.
    /// </summary>
    public SegmentedAddress CriticalErrorAddress { get => SegmentedAddress16[0x12]; set => SegmentedAddress16[0x12] = value; }

    /// <summary>
    /// Segment of PSP of parent program.
    /// </summary>
    public ushort ParentProgramSegmentPrefix { get => UInt16[0x16]; set => UInt16[0x16] = value; }

    public UInt8Array Files => GetUInt8Array(0x18, 20);

    public ushort EnvironmentTableSegment { get => UInt16[0x2C]; set => UInt16[0x2C] = value; }

    public SegmentedAddress StackPointer { get => SegmentedAddress16[0x2E]; set => SegmentedAddress16[0x2E] = value; }

    public ushort MaximumOpenFiles { get => UInt16[0x32]; set => UInt16[0x32] = value; }

    public SegmentedAddress FileTableAddress { get => SegmentedAddress16[0x34]; set => SegmentedAddress16[0x34] = value; }

    public SegmentedAddress PreviousPspAddress { get => SegmentedAddress16[0x38]; set => SegmentedAddress16[0x38] = value; }

    public byte InterimFlag { get => UInt8[0x3C]; set => UInt8[0x3C] = value; }

    public byte TrueNameFlag { get => UInt8[0x3D]; set => UInt8[0x3D] = value; }

    public ushort NNFlags { get => UInt16[0x3E]; set => UInt16[0x3E] = value; }

    public byte DosVersionMajor { get => UInt8[0x40]; set => UInt8[0x40] = value; }

    public byte DosVersionMinor { get => UInt8[0x41]; set => UInt8[0x41] = value; }

    public UInt8Array Unused => GetUInt8Array(0x42, 14);

    public UInt8Array Service => GetUInt8Array(0x50, 3);

    public UInt8Array Unused2 => GetUInt8Array(0x53, 9);

    public UInt8Array FirstFileControlBlock => GetUInt8Array(0x5C, 16);

    public UInt8Array SecondFileControlBlock => GetUInt8Array(0x6C, 16);

    public UInt8Array Unused3 => GetUInt8Array(0x7C, 4);

    public DosCommandTail DosCommandTail => new (ByteReaderWriter, BaseAddress + 0x80);
}
