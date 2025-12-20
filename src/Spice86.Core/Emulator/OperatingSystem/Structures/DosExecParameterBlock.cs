namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure;
using Spice86.Shared.Emulator.Memory;

using System.Diagnostics;

/// <summary>
/// Represents the DOS EXEC parameter block (EPB) used by INT 21h, AH=4Bh.
/// This structure is passed in ES:BX when calling the EXEC function.
/// </summary>
/// <remarks>
/// Based on MS-DOS 4.0 EXEC.ASM and RBIL documentation.
/// <para>
/// For load and execute (AL=00h) and load but don't execute (AL=01h):
/// <code>
/// Offset  Size    Description
/// 00h     WORD    Segment of environment to copy for child process (0 = use parent's)
/// 02h     DWORD   Pointer to command tail (command line arguments)
/// 06h     DWORD   Pointer to first FCB to be copied into child's PSP
/// 0Ah     DWORD   Pointer to second FCB to be copied into child's PSP
/// 0Eh     DWORD   (AL=01h only) Initial SS:SP for child process (filled in by DOS)
/// 12h     DWORD   (AL=01h only) Initial CS:IP for child process (filled in by DOS)
/// </code>
/// </para>
/// </remarks>
[DebuggerDisplay("EnvSegment={EnvironmentSegment}, CmdTail={CommandTailSegment}:{CommandTailOffset}")]
public class DosExecParameterBlock : MemoryBasedDataStructure {
    /// <summary>
    /// Size of the EXEC parameter block in bytes (for AL=00h/01h).
    /// </summary>
    public const int Size = 0x16;

    /// <summary>
    /// Initializes a new instance of the EXEC parameter block.
    /// </summary>
    /// <param name="byteReaderWriter">Where data is read and written.</param>
    /// <param name="baseAddress">The address of the structure in memory.</param>
    public DosExecParameterBlock(IByteReaderWriter byteReaderWriter, uint baseAddress) 
        : base(byteReaderWriter, baseAddress) {
    }

    /// <summary>
    /// Gets or sets the segment of environment to copy for the child process.
    /// If 0, the parent's environment is used.
    /// </summary>
    /// <remarks>Offset 0x00, 2 bytes.</remarks>
    public ushort EnvironmentSegment { get => UInt16[0x00]; set => UInt16[0x00] = value; }

    /// <summary>
    /// Gets or sets the offset portion of the pointer to the command tail.
    /// </summary>
    /// <remarks>Offset 0x02, 2 bytes.</remarks>
    public ushort CommandTailOffset { get => UInt16[0x02]; set => UInt16[0x02] = value; }

    /// <summary>
    /// Gets or sets the segment portion of the pointer to the command tail.
    /// </summary>
    /// <remarks>Offset 0x04, 2 bytes.</remarks>
    public ushort CommandTailSegment { get => UInt16[0x04]; set => UInt16[0x04] = value; }

    /// <summary>
    /// Gets the command tail pointer as a segmented address.
    /// </summary>
    public SegmentedAddress CommandTailPointer => new(CommandTailSegment, CommandTailOffset);

    /// <summary>
    /// Gets or sets the offset portion of the pointer to the first FCB.
    /// </summary>
    /// <remarks>Offset 0x06, 2 bytes.</remarks>
    public ushort FirstFcbOffset { get => UInt16[0x06]; set => UInt16[0x06] = value; }

    /// <summary>
    /// Gets or sets the segment portion of the pointer to the first FCB.
    /// </summary>
    /// <remarks>Offset 0x08, 2 bytes.</remarks>
    public ushort FirstFcbSegment { get => UInt16[0x08]; set => UInt16[0x08] = value; }

    /// <summary>
    /// Gets the first FCB pointer as a segmented address.
    /// </summary>
    public SegmentedAddress FirstFcbPointer => new(FirstFcbSegment, FirstFcbOffset);

    /// <summary>
    /// Gets or sets the offset portion of the pointer to the second FCB.
    /// </summary>
    /// <remarks>Offset 0x0A, 2 bytes.</remarks>
    public ushort SecondFcbOffset { get => UInt16[0x0A]; set => UInt16[0x0A] = value; }

    /// <summary>
    /// Gets or sets the segment portion of the pointer to the second FCB.
    /// </summary>
    /// <remarks>Offset 0x0C, 2 bytes.</remarks>
    public ushort SecondFcbSegment { get => UInt16[0x0C]; set => UInt16[0x0C] = value; }

    /// <summary>
    /// Gets the second FCB pointer as a segmented address.
    /// </summary>
    public SegmentedAddress SecondFcbPointer => new(SecondFcbSegment, SecondFcbOffset);

    /// <summary>
    /// Gets or sets the initial SP value for the child process (AL=01h only).
    /// </summary>
    /// <remarks>Offset 0x0E, 2 bytes. Filled in by DOS after loading.</remarks>
    public ushort InitialSP { get => UInt16[0x0E]; set => UInt16[0x0E] = value; }

    /// <summary>
    /// Gets or sets the initial SS value for the child process (AL=01h only).
    /// </summary>
    /// <remarks>Offset 0x10, 2 bytes. Filled in by DOS after loading.</remarks>
    public ushort InitialSS { get => UInt16[0x10]; set => UInt16[0x10] = value; }

    /// <summary>
    /// Gets or sets the initial IP value for the child process (AL=01h only).
    /// </summary>
    /// <remarks>Offset 0x12, 2 bytes. Filled in by DOS after loading.</remarks>
    public ushort InitialIP { get => UInt16[0x12]; set => UInt16[0x12] = value; }

    /// <summary>
    /// Gets or sets the initial CS value for the child process (AL=01h only).
    /// </summary>
    /// <remarks>Offset 0x14, 2 bytes. Filled in by DOS after loading.</remarks>
    public ushort InitialCS { get => UInt16[0x14]; set => UInt16[0x14] = value; }
}
