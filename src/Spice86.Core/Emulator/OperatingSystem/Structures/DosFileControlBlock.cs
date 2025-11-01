namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure.Array;

/// <summary>
/// Memory-backed representation of a DOS File Control Block (FCB).
/// <code>
/// Standard FCB (36 bytes):
///   +0x00 BYTE Drive number (0=default, 1=A, 2=B, etc.)
///   +0x01 BYTE[8] Filename (space-padded)
///   +0x09 BYTE[3] Extension (space-padded)
///   +0x0C WORD Current block number (relative to start of file)
///   +0x0E WORD Record size in bytes
///   +0x10 DWORD File size in bytes
///   +0x14 WORD Date stamp (packed format)
///   +0x16 WORD Time stamp (packed format)
///   +0x18 BYTE[8] Reserved for system use
///   +0x20 BYTE Current record within current block
///   +0x21 DWORD Random record number
/// </code>
/// </summary>
/// <remarks>
/// FCBs are used by old DOS programs for file I/O. Modern programs use file handles instead.
/// Extended FCBs have a 7-byte prefix for file attributes.
/// </remarks>
public sealed class DosFileControlBlock : MemoryBasedDataStructure {
    private const uint OffsetDriveNumber = 0x00;
    private const uint OffsetFilename = 0x01;
    private const uint OffsetExtension = 0x09;
    private const uint OffsetCurrentBlock = 0x0C;
    private const uint OffsetRecordSize = 0x0E;
    private const uint OffsetFileSize = 0x10;
    private const uint OffsetDateStamp = 0x14;
    private const uint OffsetTimeStamp = 0x16;
    private const uint OffsetReserved = 0x18;
    private const uint OffsetCurrentRecord = 0x20;
    private const uint OffsetRandomRecord = 0x21;

    /// <summary>
    /// Size of a standard FCB in bytes.
    /// </summary>
    public const int StandardSize = 37;

    /// <summary>
    /// Creates a view over a DOS FCB structure.
    /// </summary>
    /// <param name="byteReaderWriter">The memory interface to read/write FCB data.</param>
    /// <param name="baseAddress">The physical base address of the FCB in memory.</param>
    public DosFileControlBlock(IByteReaderWriter byteReaderWriter, uint baseAddress)
        : base(byteReaderWriter, baseAddress) {
    }

    /// <summary>
    /// Drive number (0=default, 1=A:, 2=B:, etc.)
    /// </summary>
    public byte DriveNumber {
        get => UInt8[OffsetDriveNumber];
        set => UInt8[OffsetDriveNumber] = value;
    }

    /// <summary>
    /// 8-character filename (space-padded, uppercase)
    /// </summary>
    public UInt8Array Filename => GetUInt8Array(OffsetFilename, 8);

    /// <summary>
    /// 3-character extension (space-padded, uppercase)
    /// </summary>
    public UInt8Array Extension => GetUInt8Array(OffsetExtension, 3);

    /// <summary>
    /// Current block number (for sequential reads)
    /// </summary>
    public ushort CurrentBlock {
        get => UInt16[OffsetCurrentBlock];
        set => UInt16[OffsetCurrentBlock] = value;
    }

    /// <summary>
    /// Logical record size in bytes (default is 128)
    /// </summary>
    public ushort RecordSize {
        get => UInt16[OffsetRecordSize];
        set => UInt16[OffsetRecordSize] = value;
    }

    /// <summary>
    /// File size in bytes
    /// </summary>
    public uint FileSize {
        get => UInt32[OffsetFileSize];
        set => UInt32[OffsetFileSize] = value;
    }

    /// <summary>
    /// DOS packed date format: bits 15-9=year (relative to 1980), 8-5=month, 4-0=day
    /// </summary>
    public ushort DateStamp {
        get => UInt16[OffsetDateStamp];
        set => UInt16[OffsetDateStamp] = value;
    }

    /// <summary>
    /// DOS packed time format: bits 15-11=hours, 10-5=minutes, 4-0=seconds/2
    /// </summary>
    public ushort TimeStamp {
        get => UInt16[OffsetTimeStamp];
        set => UInt16[OffsetTimeStamp] = value;
    }

    /// <summary>
    /// Reserved area for DOS internal use (8 bytes)
    /// </summary>
    public UInt8Array Reserved => GetUInt8Array(OffsetReserved, 8);

    /// <summary>
    /// Current record within the current block (0-127 for default record size)
    /// </summary>
    public byte CurrentRecord {
        get => UInt8[OffsetCurrentRecord];
        set => UInt8[OffsetCurrentRecord] = value;
    }

    /// <summary>
    /// Random record number for random access (32-bit)
    /// </summary>
    public uint RandomRecord {
        get => UInt32[OffsetRandomRecord];
        set => UInt32[OffsetRandomRecord] = value;
    }

    /// <summary>
    /// Clears the FCB by filling it with zeros.
    /// </summary>
    public void Clear() {
        for (uint i = 0; i < StandardSize; i++) {
            UInt8[i] = 0;
        }
    }
}
