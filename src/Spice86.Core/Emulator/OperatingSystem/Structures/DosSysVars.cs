namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.OperatingSystem.Devices;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure.Array;

/// <summary>
/// Represents the DOS core memory table (SYSVARS), containing pointers and configuration
/// values used by DOS for file management, device headers, buffers, and other system structures.
/// </summary>
/// <remarks>
/// In DOSBox, this is the 'DOS_InfoBlock' class 
/// </remarks>
public class DosSysVars : MemoryBasedDataStructure {
    private readonly DosDeviceHeader _nullDeviceHeader;
    /// <summary>
    /// Initializes a new instance of the <see cref="DosSysVars"/> class.
    /// </summary>
    /// <param name="nullDevice"></param>
    /// <param name="byteReaderWriter">The memory reader/writer.</param>
    /// <param name="baseAddress">The base address of the structure in memory.</param>
    public DosSysVars(NullDevice nullDevice, IByteReaderWriter byteReaderWriter, uint baseAddress) : base(byteReaderWriter, baseAddress) {
        _nullDeviceHeader = nullDevice.Header;
        CopyArray(_nullDeviceHeader, 0x22);
    }

    /// <summary>
    /// Gets or sets the pointer to the Disk Parameter Block (DPB) chain.
    /// </summary>
    public uint DiskParameterBlockChainPointer {
        get => UInt32[0x00];
        set => UInt32[0x00] = value;
    }

    /// <summary>
    /// Gets or sets the pointer to the System File Table (SFT) chain.
    /// </summary>
    public uint SystemFileTableChainPointer {
        get => UInt32[0x04];
        set => UInt32[0x04] = value;
    }

    /// <summary>
    /// Gets or sets the pointer to the CLOCK device header.
    /// </summary>
    public uint ClockDeviceHeaderPointer {
        get => UInt32[0x08];
        set => UInt32[0x08] = value;
    }

    /// <summary>
    /// Gets or sets the pointer to the CON device header.
    /// </summary>
    public uint ConsoleDeviceHeaderPointer {
        get => UInt32[0x0C];
        set => UInt32[0x0C] = value;
    }

    /// <summary>
    /// Gets or sets the buffer size.
    /// </summary>
    public ushort BufferSize {
        get => UInt16[0x10];
        set => UInt16[0x10] = value;
    }

    /// <summary>
    /// Gets or sets the pointer to the buffer descriptor.
    /// </summary>
    public uint BufferDescriptorPointer {
        get => UInt32[0x12];
        set => UInt32[0x12] = value;
    }

    /// <summary>
    /// Gets or sets the pointer to the Current Directory Structure (CDS) list.
    /// </summary>
    public uint CurrentDirectoryStructureListPointer {
        get => UInt32[0x16];
        set => UInt32[0x16] = value;
    }

    /// <summary>
    /// Gets or sets the pointer to the FCB System File Table chain.
    /// </summary>
    public uint FcbSystemFileTableChainPointer {
        get => UInt32[0x1A];
        set => UInt32[0x1A] = value;
    }

    /// <summary>
    /// Gets or sets the FCB keep count.
    /// </summary>
    public ushort FcbKeepCount {
        get => UInt16[0x1E];
        set => UInt16[0x1E] = value;
    }

    /// <summary>
    /// Gets or sets the DPB count.
    /// </summary>
    public byte DiskParameterBlockCount {
        get => UInt8[0x20];
        set => UInt8[0x20] = value;
    }

    /// <summary>
    /// Gets or sets the CDS count.
    /// </summary>
    public byte CurrentDirectoryStructureCount {
        get => UInt8[0x21];
        set => UInt8[0x21] = value;
    }

    /// <summary>
    /// Gets or sets the NUL device header (18 bytes).
    /// </summary>
    public DosDeviceHeader NullDeviceHeader => _nullDeviceHeader;

    private void CopyArray(DosDeviceHeader nullHeader, byte offset) {
        UInt8Array array = nullHeader.GetUInt8Array(0, DosDeviceHeader.HeaderLength);
        for (int i = 0; i < array.Count; i++) {
            byte b = array[i];
            UInt8[this.BaseAddress + offset + i] = b;
        }
    }

    /// <summary>
    /// Gets or sets the JOINed drive count.
    /// </summary>
    public byte JoinedDriveCount {
        get => UInt8[0x34];
        set => UInt8[0x34] = value;
    }

    /// <summary>
    /// pointer within IBMDOS code segment to list of special program
    /// </summary>
    /// <remarks>
    /// Unused.
    /// </remarks>
    public ushort Unused {
        get => UInt16[0x35];
        set => UInt16[0x35] = value;
    }

    /// <summary>
    /// Gets or sets the pointer to the SETVER table.
    /// </summary>
    /// <remarks>
    /// pointer to SETVER program list or 0000h:0000h
    /// </remarks>>
    public uint SetVerTablePointer {
        get => UInt32[0x37];
        set => UInt32[0x37] = value;
    }

    /// <summary>
    /// (DOS=HIGH) offset in DOS CS of function to fix A20 control when executing special .COM format
    /// </summary>
    public ushort UnknownWord3B {
        get => UInt16[0x3B];
        set => UInt16[0x3B] = value;
    }

    /// <summary>
    /// PSP of most-recently EXECed program if DOS in HMA, 0000h if low.
    /// </summary>
    /// <remarks>
    /// used for maintaining count of INT 21 calls which disable A20 on return
    /// </remarks>
    public ushort LastProgramPspSegment {
        get => UInt16[0x3D];
        set => UInt16[0x3D] = value;
    }

    /// <summary>
    /// Gets or sets the number of buffers.
    /// </summary>
    public ushort NumberOfBuffers {
        get => UInt16[0x3F];
        set => UInt16[0x3F] = value;
    }

    /// <summary>
    /// Gets or sets the number of lookahead buffers.
    /// </summary>
    public ushort NumberOfLookaheadBuffers {
        get => UInt16[0x41];
        set => UInt16[0x41] = value;
    }

    /// <summary>
    /// Gets or sets the boot drive (1 = A).
    /// </summary>
    public byte BootDrive {
        get => UInt8[0x43];
        set => UInt8[0x43] = value;
    }

    /// <summary>
    /// Gets or sets the 386 dword move flag.
    /// </summary>
    /// <remarks>
    /// flag: 01h to use DWORD moves (80386+), 00h otherwise
    /// </remarks>
    public byte DwordMoveFlag386 {
        get => UInt8[0x44];
        set => UInt8[0x44] = value;
    }

    /// <summary>
    /// Gets or sets the extended memory size.
    /// </summary>
    public ushort ExtendedMemorySize {
        get => UInt16[0x45];
        set => UInt16[0x45] = value;
    }
}
