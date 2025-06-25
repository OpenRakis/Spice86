namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.Memory.ReaderWriter;

/// <summary>
/// This structure is used by function INT 21H 0x4B (LOAD AND EXEC PROGRAM). <br/>
/// Programs that call this function pass a pointer to this structure as a parameter in ES:BX. <br/>
/// <remarks>In DOSBox source code, this is the DOS_ParamBlock</remarks>
/// </summary>
internal class DosExecParameterBlock : DosMemoryControlBlock {
    public DosExecParameterBlock(IByteReaderWriter byteReaderWriter, uint baseAddress) : base(byteReaderWriter, baseAddress) {
    }

    /// <summary>
    /// Gets or the sets the Segment (address) of the environement for the child process (0x0: duplicate the current PSP's environment)
    /// </summary>
    public ushort EnvironmentSegment { get => UInt16[0x0]; set { UInt16[0x0] = value; } }

    /// <summary>
    /// Gets or sets the address of the command line used to execute the program. Copied at the end of the child PSP.
    /// </summary>
    public uint CommandTailAddress { get => UInt32[0x2]; set { UInt32[0x2] = value; } }

    public DosCommandTail CommandTail { get => new (ByteReaderWriter, CommandTailAddress); }

    /// <summary>
    /// Gets or sets the address of the first file control block. Unopened FCB to be copied to the child PSP.
    /// </summary>
    public uint FirstFileControlBlockAddress { get => UInt32[0x6]; set { UInt32[0x6] = value; } }

    /// <summary>
    /// Gets or sets the address of the second file control block. Unopened FCB to be copied to the child PSP.
    /// </summary>
    public uint SecondFileControlBlockAddress { get => UInt32[0x10]; set { UInt32[0x10] = value; } }

    /// <summary>
    /// Gets or sets the load segment. This is where the new program will start execution.
    /// </summary>
    public ushort LoadSegment { get => UInt16[0x16]; set { UInt16[0x16] = value; } }

    /// <summary>
    /// Gets or sets the relocation segment. This influences function INT 21H 0x4B when in OVERLAY mode in the program relocation phase.
    /// </summary>
    public ushort Relocation { get => UInt16[0x18]; set { UInt16[0x18] = value; } }
}
