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
    public const int FirstMcbSegment = 0x16F;
    private readonly DosDeviceHeader _nullDeviceHeader;
    /// <summary>
    /// Initializes a new instance of the <see cref="DosSysVars"/> class.
    /// </summary>
    /// <param name="nullDevice"></param>
    /// <param name="byteReaderWriter">The memory reader/writer.</param>
    /// <param name="baseAddress">The base address of the structure in memory.</param>
    public DosSysVars(NullDevice nullDevice, IByteReaderWriter byteReaderWriter,
        uint baseAddress) : base(byteReaderWriter, baseAddress) {
        _nullDeviceHeader = nullDevice.Header;
        CopyArray(_nullDeviceHeader, 0x22);
        ClockDeviceHeaderPointer = 0x0;
        MagicWord = 0x1;
        BootDrive = 0x0;
        ExtendedMemorySize = (ushort)(byteReaderWriter.Length / 1024);
        MinMemForExec = 0x0;
        A20GateFixRoutineOffset = 0x0;
        MemAllocScanStart = FirstMcbSegment;
        MaxSectorLength = 0x200;
        RegCXfrom5e = 0x0;
        CountLRUCache = 0x0;
        CountLRUOpens = 0x0;
        SharingCount = 0x0;
        SharingDelay = 0x0;
        PtrCONInput = 0x0;
        FirstMCB = FirstMcbSegment;
        DirtyDiskBuffers = 0x0;
        LookaheadBufPt = 0x0;
        LookaheadBufNumber = 0x0;
        BufferLocation = 0x0;
        WorkspaceBuffer = 0x0;
        StartOfUMBChain = 0xFFFF;
        ChainingUMB = 0x0;
        DwordMoveFlag386 = 0x1;
        LastProgramPspSegment = 0x0;
        NumberOfBuffers = 0x50;
        NumberOfLookaheadBuffers = 0x50;
        SetVerTablePointer = 0x0;
        JoinedDriveCount = 0x0;
        DiskParameterBlockCount = 0x1;
        DiskBufferPointer = 0x0;
        SpecialCodeSegment = 0x0;
    }

    public ushort MagicWord {
        get => UInt16[-0x22];
        set => UInt16[-0x22] = value;
    }

    /// <summary>
    /// Gets or sets CX from last int21/ah=5e
    /// </summary>
    public ushort RegCXfrom5e {
        get => UInt16[-0x18];
        set => UInt16[-0x18] = value;
    }

    /// <summary>
    /// Gets or sets LRU counter for FCB caching
    /// </summary>
    public ushort CountLRUCache {
        get => UInt16[-0x16];
        set => UInt16[-0x16] = value;
    }

    /// <summary>
    /// Gets or sets LRU counter for FCB openings
    /// </summary>
    public ushort CountLRUOpens {
        get => UInt16[-0x14];
        set => UInt16[-0x14] = value;
    }

    /// <summary>
    /// Gets or sets sharing retry count
    /// </summary>
    public ushort SharingCount {
        get => UInt16[-0x0C];
        set => UInt16[-0x0C] = value;
    }

    /// <summary>
    /// Gets or sets sharing retry delay
    /// </summary>
    public ushort SharingDelay {
        get => UInt16[-0x0A];
        set => UInt16[-0x0A] = value;
    }

    /// <summary>
    /// Gets or sets pointer to disk buffer
    /// </summary>
    public uint DiskBufferPointer {
        get => UInt32[-0x08];
        set => UInt32[-0x08] = value;
    }

    /// <summary>
    /// Gets or sets pointer to CON input
    /// </summary>
    public ushort PtrCONInput {
        get => UInt16[-0x04];
        set => UInt16[-0x04] = value;
    }

    /// <summary>
    /// Gets or sets first memory control block
    /// </summary>
    public ushort FirstMCB {
        get => UInt16[-0x02];
        set => UInt16[-0x02] = value;
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
    /// Gets or sets maximum bytes per sector of any block device
    /// </summary>
    public ushort MaxSectorLength {
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
    public ushort SpecialCodeSegment {
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
    public ushort A20GateFixRoutineOffset {
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

    /// <summary>
    /// Gets or sets the pointer to least-recently used buffer header
    /// </summary>
    public uint DiskBufferHeadPt {
        get => UInt32[0x47];
        set => UInt32[0x47] = value;
    }

    /// <summary>
    /// Gets or sets the number of dirty disk buffers
    /// </summary>
    public ushort DirtyDiskBuffers {
        get => UInt16[0x4B];
        set => UInt16[0x4B] = value;
    }

    /// <summary>
    /// Gets or sets the pointer to lookahead buffer
    /// </summary>
    public uint LookaheadBufPt {
        get => UInt32[0x4D];
        set => UInt32[0x4D] = value;
    }

    /// <summary>
    /// Gets or sets the number of lookahead buffers
    /// </summary>
    public ushort LookaheadBufNumber {
        get => UInt16[0x51];
        set => UInt16[0x51] = value;
    }

    /// <summary>
    /// Gets or sets the workspace buffer location
    /// </summary>
    public byte BufferLocation {
        get => UInt8[0x53];
        set => UInt8[0x53] = value;
    }

    /// <summary>
    /// Gets or sets the pointer to workspace buffer
    /// </summary>
    public uint WorkspaceBuffer {
        get => UInt32[0x54];
        set => UInt32[0x54] = value;
    }

    /// <summary>
    /// Gets or sets bit0: UMB chain linked to MCB chain
    /// </summary>
    public byte ChainingUMB {
        get => UInt8[0x63];
        set => UInt8[0x63] = value;
    }

    /// <summary>
    /// Gets or sets the minimum paragraphs needed for current program
    /// </summary>
    public ushort MinMemForExec {
        get => UInt16[0x64];
        set => UInt16[0x64] = value;
    }

    /// <summary>
    /// Gets or sets the segment of first UMB-MCB
    /// </summary>
    public ushort StartOfUMBChain {
        get => UInt16[0x66];
        set => UInt16[0x66] = value;
    }

    /// <summary>
    /// Gets or sets the start paragraph for memory allocation
    /// </summary>
    public ushort MemAllocScanStart {
        get => UInt16[0x68];
        set => UInt16[0x68] = value;
    }
}
