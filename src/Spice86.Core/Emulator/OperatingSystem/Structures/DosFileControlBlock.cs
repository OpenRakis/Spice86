namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure;

using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Text;

/// <summary>
/// Represents a DOS File Control Block (FCB) in memory.
/// FCBs are used by DOS functions 0x0F-0x24 for file operations.
/// This is a 37-byte structure used primarily in DOS 1.x and 2.x programs.
/// </summary>
[DebuggerDisplay("Drive = {DriveNumber}, FileName = {FileName}, Extension = {FileExtension}")]
public class DosFileControlBlock : MemoryBasedDataStructure {
    private const int DriveNumberOffset = 0;
    private const int FileNameOffset = 1;
    private const int FileExtensionOffset = 9;
    private const int CurrentBlockOffset = 12;
    private const int RecordSizeOffset = 14;
    private const int FileSizeOffset = 16;
    private const int LastWriteDateOffset = 20;
    private const int LastWriteTimeOffset = 22;
    private const int ReservedOffset = 24;
    private const int CurrentRecordOffset = 32;
    private const int RandomRecordOffset = 33;

    public const int FCBSize = 37;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="byteReaderWriter">Where data is read and written.</param>
    /// <param name="baseAddress">The address of the FCB structure in memory.</param>
    public DosFileControlBlock(IByteReaderWriter byteReaderWriter, uint baseAddress) : base(byteReaderWriter, baseAddress) {
    }

    /// <summary>
    /// Gets or sets the drive number (0 = default, 1 = A:, 2 = B:, etc.)
    /// </summary>
    public byte DriveNumber {
        get => UInt8[DriveNumberOffset];
        set => UInt8[DriveNumberOffset] = value;
    }

    /// <summary>
    /// Gets or sets the filename (8 characters, space-padded)
    /// </summary>
    [Range(0, 8)]
    public string FileName {
        get {
            byte[] nameBytes = new byte[8];
            for (int i = 0; i < 8; i++) {
                nameBytes[i] = UInt8[FileNameOffset + i];
            }
            return Encoding.ASCII.GetString(nameBytes).TrimEnd();
        }
        set {
            string paddedName = (value ?? "").PadRight(8).Substring(0, 8).ToUpperInvariant();
            byte[] nameBytes = Encoding.ASCII.GetBytes(paddedName);
            for (int i = 0; i < 8; i++) {
                UInt8[FileNameOffset + i] = nameBytes[i];
            }
        }
    }

    /// <summary>
    /// Gets or sets the file extension (3 characters, space-padded)
    /// </summary>
    [Range(0, 3)]
    public string FileExtension {
        get {
            byte[] extBytes = new byte[3];
            for (int i = 0; i < 3; i++) {
                extBytes[i] = UInt8[FileExtensionOffset + i];
            }
            return Encoding.ASCII.GetString(extBytes).TrimEnd();
        }
        set {
            string paddedExt = (value ?? "").PadRight(3).Substring(0, 3).ToUpperInvariant();
            byte[] extBytes = Encoding.ASCII.GetBytes(paddedExt);
            for (int i = 0; i < 3; i++) {
                UInt8[FileExtensionOffset + i] = extBytes[i];
            }
        }
    }

    /// <summary>
    /// Gets or sets the current block number
    /// </summary>
    public ushort CurrentBlock {
        get => UInt16[CurrentBlockOffset];
        set => UInt16[CurrentBlockOffset] = value;
    }

    /// <summary>
    /// Gets or sets the record size (default 128 bytes)
    /// </summary>
    public ushort RecordSize {
        get => UInt16[RecordSizeOffset];
        set => UInt16[RecordSizeOffset] = value;
    }

    /// <summary>
    /// Gets or sets the file size in bytes
    /// </summary>
    public uint FileSize {
        get => UInt32[FileSizeOffset];
        set => UInt32[FileSizeOffset] = value;
    }

    /// <summary>
    /// Gets or sets the last write date (DOS format)
    /// </summary>
    public ushort LastWriteDate {
        get => UInt16[LastWriteDateOffset];
        set => UInt16[LastWriteDateOffset] = value;
    }

    /// <summary>
    /// Gets or sets the last write time (DOS format)
    /// </summary>
    public ushort LastWriteTime {
        get => UInt16[LastWriteTimeOffset];
        set => UInt16[LastWriteTimeOffset] = value;
    }

    /// <summary>
    /// Gets or sets the current record within the current block
    /// </summary>
    public byte CurrentRecord {
        get => UInt8[CurrentRecordOffset];
        set => UInt8[CurrentRecordOffset] = value;
    }

    /// <summary>
    /// Gets or sets the random record number (32-bit)
    /// </summary>
    public uint RandomRecord {
        get => UInt32[RandomRecordOffset];
        set => UInt32[RandomRecordOffset] = value;
    }

    /// <summary>
    /// Gets the full filename with extension (e.g., "FILENAME.EXT")
    /// </summary>
    public string FullFileName {
        get {
            string name = FileName.TrimEnd();
            string ext = FileExtension.TrimEnd();
            if (string.IsNullOrEmpty(ext)) {
                return name;
            }
            return $"{name}.{ext}";
        }
    }

    /// <summary>
    /// Initializes the FCB with default values
    /// </summary>
    public void Initialize() {
        DriveNumber = 0;
        FileName = "        "; // 8 spaces
        FileExtension = "   "; // 3 spaces
        CurrentBlock = 0;
        RecordSize = 128; // Default DOS record size
        FileSize = 0;
        LastWriteDate = 0;
        LastWriteTime = 0;
        CurrentRecord = 0;
        RandomRecord = 0;

        // Clear reserved area
        for (int i = ReservedOffset; i < CurrentRecordOffset; i++) {
            UInt8[i] = 0;
        }
    }

    /// <inheritdoc />
    public override string ToString() {
        return new StringBuilder("FCB: ")
            .Append("Drive=").Append(DriveNumber)
            .Append(" FileName=").Append(FullFileName)
            .Append(" Size=").Append(FileSize)
            .Append(" RecordSize=").Append(RecordSize)
            .ToString();
    }
}