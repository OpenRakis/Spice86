namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure;

/// <summary>
/// Represents a DTA (Disk Transfer Area) in memory.
/// </summary>
public class DosDiskTransferArea : MemoryBasedDataStructure {
    /// <summary>
    /// Initializes a new instance of the <see cref="DosDiskTransferArea"/> class.
    /// </summary>
    /// <param name="byteReaderWriter">The memory bus used for accessing the DTA.</param>
    /// <param name="baseAddress">The base address of the DTA within memory.</param>
    public DosDiskTransferArea(IByteReaderWriter byteReaderWriter, uint baseAddress) : base(byteReaderWriter, baseAddress) {
    }
    
    /// <summary>
    /// The offset in bytes where the attribute field is located within the FileMatch structure.
    /// </summary>
    private const int AttributeOffset = 0x15;

    /// <summary>
    /// The offset in bytes where the file date field is located within the FileMatch structure.
    /// </summary>
    private const int FileDateOffset = 0x18;

    /// <summary>
    /// The offset in bytes where the file name field is located within the FileMatch structure.
    /// </summary>
    private const int FileNameOffset = 0x1E;

    /// <summary>
    /// The size in bytes of the file name field within the FileMatch structure.
    /// </summary>
    private const int FileNameSize = 13;

    /// <summary>
    /// The offset in bytes where the file size field is located within the FileMatch structure.
    /// </summary>
    private const int FileSizeOffset = 0x1A;

    /// <summary>
    /// The offset in bytes where the file time field is located within the FileMatch structure.
    /// </summary>
    private const int FileTimeOffset = 0x16;

    /// <summary>
    /// The offset in bytes where the reserved data is located within the FileMatch structure.
    /// </summary>
    private const int ReservedOffset = 0x0;
    
    /// <summary>
    /// Data used by the DOS kernel for private book keeping.
    /// <remarks>No one should touch this, apart from DOS.</remarks>
    /// </summary>
    public ushort Reserved { get => UInt16[ReservedOffset]; set => UInt16[ReservedOffset] = value; }

    /// <summary>
    /// Gets or sets the file attributes field of the FileMatch structure.
    /// </summary>
    public byte FileAttributes { get => UInt8[AttributeOffset]; set => UInt8[AttributeOffset] = value; }

    /// <summary>
    /// Gets or sets the file date field of the FileMatch structure.
    /// </summary>
    public ushort FileDate { get => UInt16[FileDateOffset]; set => UInt16[FileDateOffset] = value; }

    /// <summary>
    /// Gets or sets the file name field of the FileMatch structure.
    /// </summary>
    public string FileName {
        get => GetZeroTerminatedString(FileNameOffset, FileNameSize);
        set => SetZeroTerminatedString(FileNameOffset, value, FileNameSize);
    }

    /// <summary>
    /// Gets or sets the file size field of the FileMatch structure.
    /// </summary>
    public ushort FileSize { get => UInt16[FileSizeOffset]; set => UInt16[FileSizeOffset] = value; }

    /// <summary>
    /// Gets or sets the file time field of the FileMatch structure.
    /// </summary>
    public ushort FileTime { get => UInt16[FileTimeOffset]; set => UInt16[FileTimeOffset] = value; }
}