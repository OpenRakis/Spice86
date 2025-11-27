namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure.Array;

using System.Diagnostics;

/// <summary>
/// Represents the Program Segment Prefix (PSP), a 256-byte header that DOS creates
/// for each running program.
/// </summary>
/// <remarks>
/// <para>
/// The PSP is always located at offset 0 of the program's memory allocation,
/// with the program code starting at offset 100h (for COM files) or after the PSP
/// (for EXE files).
/// </para>
/// <para>
/// PSP structure (256 bytes):
/// <code>
/// Offset  Size  Description
/// 00h     2B    INT 20h instruction (CP/M-style exit)
/// 02h     WORD  Segment of first byte beyond program allocation
/// 04h     BYTE  Reserved
/// 05h     5B    Far call to DOS dispatcher (obsolete)
/// 0Ah     DWORD Terminate address (INT 22h vector)
/// 0Eh     DWORD Ctrl-C handler address (INT 23h vector)
/// 12h     DWORD Critical error handler address (INT 24h vector)
/// 16h     WORD  Parent PSP segment
/// 18h     20B   Job File Table (file handle array)
/// 2Ch     WORD  Environment segment
/// 2Eh     DWORD SS:SP on entry to last INT 21h call
/// 32h     WORD  Maximum file handles
/// 34h     DWORD Pointer to Job File Table
/// 38h     DWORD Previous PSP (for nested command interpreters)
/// 3Ch     BYTE  Interim console flag
/// 3Dh     BYTE  Truename flag
/// 3Eh     WORD  NextPSP sharing file handles
/// 40h     WORD  DOS version to return
/// 42h     14B   Reserved
/// 50h     3B    DOS function dispatcher (INT 21h, RETF)
/// 53h     9B    Reserved
/// 5Ch     16B   Default FCB #1
/// 6Ch     16B   Default FCB #2 (overlaps FCB #1)
/// 7Ch     4B    Reserved
/// 80h     128B  Command tail (parameter length + command line + CR)
/// </code>
/// </para>
/// <para>
/// <strong>FreeDOS vs MS-DOS PSP Behavior Notes:</strong>
/// <list type="bullet">
/// <item>
/// <term>Parent PSP (offset 16h):</term>
/// <description>When a process is its own parent (PSP segment == parent PSP segment),
/// it indicates the root of the PSP chain (typically COMMAND.COM). FreeDOS and MS-DOS
/// treat self-parented processes slightly differently during INT 24h abort. See
/// https://github.com/FDOS/kernel/issues/213 for details.</description>
/// </item>
/// <item>
/// <term>Environment Block (offset 2Ch):</term>
/// <description>The environment block is a separate MCB owned by the process.
/// When the process terminates, this MCB is freed along with the program's memory.
/// FreeDOS allocates the environment block immediately before the PSP.</description>
/// </item>
/// <item>
/// <term>Job File Table (offset 18h and 34h):</term>
/// <description>The internal JFT at offset 18h holds 20 file handles by default.
/// The pointer at 34h normally points to offset 18h. Programs can expand the JFT
/// by allocating memory and updating the pointer at 34h and count at 32h.</description>
/// </item>
/// <item>
/// <term>Interrupt Vectors (offsets 0Ah-15h):</term>
/// <description>When a program terminates, DOS restores INT 22h, 23h, and 24h
/// from the values stored in the terminating process's PSP. This allows each
/// program to have its own handlers that are restored on exit.</description>
/// </item>
/// </list>
/// </para>
/// </remarks>
[DebuggerDisplay("BaseAddress={BaseAddress}, Parent={ParentProgramSegmentPrefix}, EnvSegment={EnvironmentTableSegment}, NextSegment={NextSegment}, StackPointer={StackPointer}, Cmd={DosCommandTail.Command}")]
public class DosProgramSegmentPrefix : MemoryBasedDataStructure {
    public const ushort MaxLength = 0x80 + 128;

    public DosProgramSegmentPrefix(IByteReaderWriter byteReaderWriter, uint baseAddress) : base(byteReaderWriter, baseAddress) {
    }

    /// <summary>
    /// CP/M like exit point for INT 0x20. (machine code: 0xCD, 0x20). Old way to exit the program.
    /// </summary>
    public UInt8Array Exit => GetUInt8Array(0x0, 2);

    /// <summary>
    /// Segment of first byte beyond the end of the program image. Reserved.
    /// </summary>
    public ushort NextSegment { get => UInt16[0x2]; set => UInt16[0x2] = value; }

    /// <summary>
    /// Reserved
    /// </summary>
    public byte Reserved { get => UInt8[0x4]; set => UInt8[0x4] = value; }

    /// <summary>
    /// Far call to DOS INT 0x21 dispatcher. Obsolete.
    /// </summary>
    public byte FarCall { get => UInt8[0x5]; set => UInt8[0x5] = value; }

    public uint CpmServiceRequestAddress { get => UInt32[0x6]; set => UInt32[0x6] = value; }

    /// <summary>
    /// On exit, DOS copies this to the INT 0x22 vector.
    /// </summary>
    public uint TerminateAddress { get => UInt32[0x0A]; set => UInt32[0x0A] = value; }

    /// <summary>
    /// On exit, DOS copies this to the INT 0x23 vector.
    /// </summary>
    public uint BreakAddress { get => UInt32[0x0E]; set => UInt32[0x0E] = value; }

    /// <summary>
    /// On exit, DOS copies this to the INT 0x24 vector.
    /// </summary>
    public uint CriticalErrorAddress { get => UInt32[0x12]; set => UInt32[0x12] = value; }

    /// <summary>
    /// Segment of PSP of parent program.
    /// </summary>
    public ushort ParentProgramSegmentPrefix { get => UInt16[0x16]; set => UInt16[0x16] = value; }

    public UInt8Array Files => GetUInt8Array(0x18, 20);

    public ushort EnvironmentTableSegment { get => UInt16[0x2C]; set => UInt16[0x2C] = value; }

    public uint StackPointer { get => UInt32[0x2E]; set => UInt32[0x2E] = value; }

    public ushort MaximumOpenFiles { get => UInt16[0x32]; set => UInt16[0x32] = value; }

    public uint FileTableAddress { get => UInt32[0x34]; set => UInt32[0x34] = value; }

    public uint PreviousPspAddress { get => UInt32[0x38]; set => UInt32[0x38] = value; }

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

    public DosCommandTail DosCommandTail => new(ByteReaderWriter, BaseAddress + 0x80);
}