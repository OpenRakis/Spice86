namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure;

using System.Text;

/// <summary>
/// Represents a DOS File Control Block (FCB) in memory - a legacy CP/M-style data structure for file operations.
/// </summary>
/// <remarks>
/// <para>
/// <b>What is an FCB?</b>
/// The File Control Block is a 37-byte data structure that applications use to perform file operations
/// in DOS without using file handles. It originated in CP/M and was retained in DOS for compatibility.
/// Modern DOS programs use file handles (INT 21h AH=3Ch-46h), but FCBs are still required for many
/// older programs, games, and utilities from the 1980s-early 1990s (e.g., Civilization, Reunion, Detroit).
/// </para>
/// <para>
/// <b>How FCBs Work:</b>
/// 1. Application allocates 37 bytes in its memory for the FCB
/// 2. Application fills in the filename and extension (drive is optional)
/// 3. Application calls INT 21h with AH=0Fh (Open) or AH=16h (Create), passing DS:DX pointing to the FCB
/// 4. DOS fills in the rest of the FCB fields (size, date, time, internal handle)
/// 5. Application performs reads/writes by calling INT 21h with the FCB pointer
/// 6. DOS tracks file position using CurrentBlock/CurrentRecord (sequential) or RandomRecord (random access)
/// 7. Application calls INT 21h AH=10h (Close) when done
/// </para>
/// <para>
/// <b>FCB Structure Layout (37 bytes):</b></para>
/// <list type="table">
///   <listheader><term>Offset</term><term>Size</term><term>Field</term><term>Description</term></listheader>
///   <item><term>0x00</term><term>1</term><term>Drive</term><term>0=default, 1=A:, 2=B:, 0xFF=Extended FCB marker</term></item>
///   <item><term>0x01</term><term>8</term><term>Filename</term><term>Space-padded, uppercase, no dot (e.g., "MYFILE  ")</term></item>
///   <item><term>0x09</term><term>3</term><term>Extension</term><term>Space-padded, uppercase (e.g., "DAT")</term></item>
///   <item><term>0x0C</term><term>2</term><term>CurrentBlock</term><term>Block number for sequential I/O (1 block = 128 records)</term></item>
///   <item><term>0x0E</term><term>2</term><term>RecordSize</term><term>Bytes per record (default 128 if 0, can be set to any value)</term></item>
///   <item><term>0x10</term><term>4</term><term>FileSize</term><term>File size in bytes (maintained by DOS)</term></item>
///   <item><term>0x14</term><term>2</term><term>Date</term><term>Last write date: bits 15-9=year-1980, 8-5=month, 4-0=day</term></item>
///   <item><term>0x16</term><term>2</term><term>Time</term><term>Last write time: bits 15-11=hour, 10-5=minute, 4-0=seconds/2</term></item>
///   <item><term>0x18</term><term>8</term><term>Reserved</term><term>Internal DOS use (SFT handle, search state, etc.)</term></item>
///   <item><term>0x20</term><term>1</term><term>CurrentRecord</term><term>Record within block (0-127) for sequential I/O</term></item>
///   <item><term>0x21</term><term>4</term><term>RandomRecord</term><term>Absolute record number for random access (32-bit)</term></item>
/// </list>
/// <para>
/// <b>Sequential vs Random Access:</b>
/// <list type="bullet">
///   <item><b>Sequential:</b> Uses CurrentBlock and CurrentRecord. Each read/write advances the position.
///         INT 21h AH=14h (sequential read), AH=15h (sequential write).</item>
///   <item><b>Random:</b> Uses RandomRecord. Application sets RandomRecord before each operation.
///         INT 21h AH=21h (random read), AH=22h (random write), AH=27h/28h (block read/write).</item>
///   <item>Use INT 21h AH=24h to convert CurrentBlock/CurrentRecord to RandomRecord (and vice versa via AH=21h).</item>
/// </list>
/// </para>
/// <para>
/// <b>Implementation Notes:</b>
/// <list type="bullet">
///   <item>The Reserved area (0x18-0x1F) stores the SFT (System File Table) handle at offset 0x18 (SftNumber property).</item>
/// </list>
/// </para>
/// </remarks>
public class DosFileControlBlock : MemoryBasedDataStructure {
    /// <summary>
    /// Total size of an FCB structure in bytes.
    /// </summary>
    public virtual int StructureSize => 37;

    /// <summary>
    /// Default record size for FCB operations.
    /// </summary>
    public const ushort DefaultRecordSize = 128;

    /// <summary>
    /// Maximum file name length (8 characters).
    /// </summary>
    public const int FileNameSize = 8;

    /// <summary>
    /// Maximum file extension length (3 characters).
    /// </summary>
    public const int FileExtensionSize = 3;

    // Field offsets
    private const int DriveNumberOffset = 0x00;
    private const int FileNameOffset = 0x01;
    private const int FileExtensionOffset = 0x09;
    private const int CurrentBlockOffset = 0x0C;
    private const int RecordSizeOffset = 0x0E;
    private const int FileSizeOffset = 0x10;
    private const int DateOffset = 0x14;
    private const int TimeOffset = 0x16;
    private const int ReservedOffset = 0x18;
    private const int CurrentRecordOffset = 0x20;
    private const int RandomRecordOffset = 0x21;

    /// <summary>
    /// Initializes a new instance of the <see cref="DosFileControlBlock"/> class.
    /// </summary>
    /// <param name="byteReaderWriter">The memory bus for reading/writing FCB data.</param>
    /// <param name="baseAddress">The base address of the FCB in memory.</param>
    public DosFileControlBlock(IByteReaderWriter byteReaderWriter, uint baseAddress)
        : base(byteReaderWriter, baseAddress) {
    }

    /// <summary>
    /// Gets or sets the drive number.
    /// 0 = default drive, 1 = A:, 2 = B:, etc.
    /// </summary>
    public byte DriveNumber {
        get => UInt8[DriveNumberOffset];
        set => UInt8[DriveNumberOffset] = value;
    }

    /// <summary>
    /// Gets or sets the file name (8 characters, space-padded).
    /// </summary>
    public string FileName {
        get => GetSpacePaddedString(FileNameOffset, FileNameSize);
        set => SetSpacePaddedString(FileNameOffset, value, FileNameSize);
    }

    /// <summary>
    /// Gets or sets the file extension (3 characters, space-padded).
    /// </summary>
    public string FileExtension {
        get => GetSpacePaddedString(FileExtensionOffset, FileExtensionSize);
        set => SetSpacePaddedString(FileExtensionOffset, value, FileExtensionSize);
    }

    /// <summary>
    /// Gets or sets the current block number.
    /// Each block contains 128 records.
    /// </summary>
    public ushort CurrentBlock {
        get => UInt16[CurrentBlockOffset];
        set => UInt16[CurrentBlockOffset] = value;
    }

    /// <summary>
    /// Gets or sets the logical record size in bytes.
    /// Default is 128 bytes.
    /// </summary>
    public ushort RecordSize {
        get => UInt16[RecordSizeOffset];
        set => UInt16[RecordSizeOffset] = value;
    }

    /// <summary>
    /// Gets or sets the file size in bytes.
    /// </summary>
    public uint FileSize {
        get => UInt32[FileSizeOffset];
        set => UInt32[FileSizeOffset] = value;
    }

    /// <summary>
    /// Gets or sets the file date in DOS format.
    /// </summary>
    public ushort Date {
        get => UInt16[DateOffset];
        set => UInt16[DateOffset] = value;
    }

    /// <summary>
    /// Gets or sets the file time in DOS format.
    /// </summary>
    public ushort Time {
        get => UInt16[TimeOffset];
        set => UInt16[TimeOffset] = value;
    }

    /// <summary>
    /// Gets or sets the SFT (System File Table) number.
    /// This is used internally by DOS to track the open file.
    /// </summary>
    public byte SftNumber {
        get => UInt8[ReservedOffset];
        set => UInt8[ReservedOffset] = value;
    }

    /// <summary>
    /// Gets or sets the high byte of device attributes.
    /// </summary>
    public byte AttributeHigh {
        get => UInt8[ReservedOffset + 1];
        set => UInt8[ReservedOffset + 1] = value;
    }

    /// <summary>
    /// Gets or sets the low byte of device attributes.
    /// </summary>
    public byte AttributeLow {
        get => UInt8[ReservedOffset + 2];
        set => UInt8[ReservedOffset + 2] = value;
    }

    /// <summary>
    /// Gets or sets the starting cluster of the file.
    /// </summary>
    public ushort StartCluster {
        get => UInt16[ReservedOffset + 3];
        set => UInt16[ReservedOffset + 3] = value;
    }

    /// <summary>
    /// Gets or sets the cluster of the directory entry.
    /// </summary>
    public ushort DirectoryCluster {
        get => UInt16[ReservedOffset + 5];
        set => UInt16[ReservedOffset + 5] = value;
    }

    /// <summary>
    /// Gets or sets the offset of the directory entry (unused).
    /// </summary>
    public byte DirectoryOffset {
        get => UInt8[ReservedOffset + 7];
        set => UInt8[ReservedOffset + 7] = value;
    }

    /// <summary>
    /// Gets or sets the current record number within the current block.
    /// </summary>
    public byte CurrentRecord {
        get => UInt8[CurrentRecordOffset];
        set => UInt8[CurrentRecordOffset] = value;
    }

    /// <summary>
    /// Gets or sets the random record number for random I/O operations.
    /// </summary>
    public uint RandomRecord {
        get => UInt32[RandomRecordOffset];
        set => UInt32[RandomRecordOffset] = value;
    }

    /// <summary>
    /// Gets the absolute record number based on current block and record.
    /// </summary>
    public uint AbsoluteRecord => (uint)CurrentBlock * 128 + CurrentRecord;

    /// <summary>
    /// Gets the full 8.3 file name as "FILENAME.EXT" format.
    /// </summary>
    public string FullFileName {
        get {
            string name = FileName.TrimEnd();
            string ext = FileExtension.TrimEnd();
            return string.IsNullOrEmpty(ext) ? name : $"{name}.{ext}";
        }
    }

    /// <summary>
    /// Advances to the next record, updating block if necessary.
    /// </summary>
    public void NextRecord() {
        if (++CurrentRecord >= 128) {
            CurrentRecord = 0;
            CurrentBlock++;
        }
    }

    /// <summary>
    /// Sets the current block and record from a random record number.
    /// </summary>
    public void CalculateRecordPosition() {
        CurrentBlock = (ushort)(RandomRecord / 128);
        CurrentRecord = (byte)(RandomRecord % 128);
    }

    /// <summary>
    /// Sets the random record number from current block and record.
    /// </summary>
    public void SetRandomFromPosition() {
        RandomRecord = AbsoluteRecord;
    }

    /// <summary>
    /// Gets a space-padded string from memory.
    /// </summary>
    /// <param name="offset">The offset from the base address.</param>
    /// <param name="length">The fixed length of the string field.</param>
    /// <returns>The string, including trailing spaces.</returns>
    private string GetSpacePaddedString(int offset, int length) {
        StringBuilder result = new();
        for (int i = 0; i < length; i++) {
            byte b = UInt8[(uint)offset + (uint)i];
            result.Append((char)b);
        }
        return result.ToString();
    }

    /// <summary>
    /// Sets a space-padded string in memory.
    /// </summary>
    /// <param name="offset">The offset from the base address.</param>
    /// <param name="value">The string value to write.</param>
    /// <param name="length">The fixed length of the string field.</param>
    private void SetSpacePaddedString(int offset, string value, int length) {
        byte[] bytes = Encoding.ASCII.GetBytes(value.PadRight(length));
        for (int i = 0; i < length; i++) {
            UInt8[(uint)offset + (uint)i] = bytes[i];
        }
    }
}
