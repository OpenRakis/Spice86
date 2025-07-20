﻿namespace Spice86.Core.Emulator.OperatingSystem.Structures;

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
    /// The offset in bytes where we store on which drive the search is performed.
    /// </summary>
    private const int DriveOffset = 0x0;

    /// <summary>
    /// The offset in bytes where the SearchId is located.
    /// </summary>
    private const int SearchIdOffset = 0x13;

    /// <summary>
    /// The offset in bytes where the attribute field is located.
    /// </summary>
    private const int AttributeOffset = 0x15;

    /// <summary>
    /// The offset in bytes where the file time field is located.
    /// </summary>
    private const int FileTimeOffset = 0x16;

    /// <summary>
    /// The offset in bytes where the file date field is located.
    /// </summary>
    private const int FileDateOffset = 0x18;

    /// <summary>
    /// The offset in bytes where the file size field is located.
    /// </summary>
    private const int FileSizeOffset = 0x1A;

    /// <summary>
    /// The offset in bytes where the file name field is located.
    /// </summary>
    private const int FileNameOffset = 0x1E;

    /// <summary>
    /// The size in bytes of the zero-terminated ASCII file name string field.
    /// </summary>
    private const int FileNameSize = 13;

    /// <summary>
    /// Gets or sets the drive on which the search is performed.
    /// <remarks>No one should touch this, except DOS.</remarks>
    /// </summary>
    internal byte Drive { get => UInt8[DriveOffset]; set => UInt8[DriveOffset] = value; }

    /// <summary>
    /// The file attributes used by the file search.
    /// <remarks>No one should touch this, except DOS.</remarks>
    /// </summary>
    internal byte SearchAttributes { get => UInt8[0x12]; set => UInt8[0x12] = value; }

    /// <summary>
    /// The SearchId, for multiple searches at the same time.
    /// <remarks>No one should touch this, except DOS.</remarks>
    /// </summary>
    internal byte SearchId { get => UInt8[SearchIdOffset]; set => UInt8[SearchIdOffset] = value; }

    /// <summary>
    /// Gets or sets where we are in the enumeration of the search results.
    /// <remarks>No one should touch this, except DOS.</remarks>
    /// </summary>
    internal ushort EntryCountWithinSearchResults { get => UInt16[0xD]; set => UInt16[0xD] = value; }

    /// <summary>
    /// Gets or sets the file attributes field.
    /// </summary>
    public byte FileAttributes { get => UInt8[AttributeOffset]; set => UInt8[AttributeOffset] = value; }

    /// <summary>
    /// Gets or sets the file time field.
    /// </summary>
    public ushort FileTime { get => UInt16[FileTimeOffset]; set => UInt16[FileTimeOffset] = value; }

    /// <summary>
    /// Gets or sets the file date field.
    /// </summary>
    public ushort FileDate { get => UInt16[FileDateOffset]; set => UInt16[FileDateOffset] = value; }

    /// <summary>
    /// Gets or sets the file size field.
    /// </summary>
    public ushort FileSize { get => UInt16[FileSizeOffset]; set => UInt16[FileSizeOffset] = value; }

    /// <summary>
    /// Gets or sets the file name field.
    /// </summary>
    public string FileName {
        get => GetZeroTerminatedString(FileNameOffset, FileNameSize);
        set => SetZeroTerminatedString(FileNameOffset, value, FileNameSize);
    }
}