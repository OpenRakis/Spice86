namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.OperatingSystem.Enums;
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
    /// The offset for the search pattern name in the DTA.
    /// </summary>
    private const int SearchNameOffset = 0x1;

    /// <summary>
    /// The offset for the search pattern extension in the DTA.
    /// </summary>
    private const int SearchExtOffset = 0x9;

    /// <summary>
    /// Gets or sets the drive on which the search is performed.
    /// <remarks>No one should touch this, except DOS.</remarks>
    /// </summary>
    internal byte Drive { get => UInt8[DriveOffset]; set => UInt8[DriveOffset] = value; }

    /// <summary>
    /// The file attributes used by the file search.
    /// <remarks>No one should touch this, except DOS.</remarks>
    /// </summary>
    internal byte SearchAttributes { get => UInt8[AttributeOffset - 1]; set => UInt8[AttributeOffset - 1] = value; }

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
    /// Gets or sets the file size field as a uint32 (4 bytes).
    /// </summary>
    public uint FileSize { 
        get => UInt32[FileSizeOffset]; 
        set => UInt32[FileSizeOffset] = value;
    }

    /// <summary>
    /// Gets or sets the file name field.
    /// </summary>
    public string FileName {
        get => GetZeroTerminatedString(FileNameOffset, FileNameSize);
        set => SetZeroTerminatedString(FileNameOffset, value, FileNameSize);
    }

    /// <summary>
    /// Gets or sets the search name pattern (8 characters).
    /// </summary>
    public string SearchName {
        get {
            // Reading from raw memory to get the actual bytes (may include spaces)
            byte[] nameBytes = new byte[8];
            for (int i = 0; i < 8; i++) {
                nameBytes[i] = UInt8[SearchNameOffset + i];
            }
            return System.Text.Encoding.ASCII.GetString(nameBytes);
        }
        set {
            // Ensure exactly 8 characters, space-padded if necessary
            string paddedName = value.Length >= 8 ? value[..8] : value.PadRight(8, ' ');
            byte[] nameBytes = System.Text.Encoding.ASCII.GetBytes(paddedName);
            for (int i = 0; i < 8; i++) {
                UInt8[SearchNameOffset + i] = nameBytes[i];
            }
        }
    }

    /// <summary>
    /// Gets or sets the search extension pattern (3 characters).
    /// </summary>
    public string SearchExtension {
        get {
            // Reading from raw memory to get the actual bytes (may include spaces)
            byte[] extBytes = new byte[3];
            for (int i = 0; i < 3; i++) {
                extBytes[i] = UInt8[SearchExtOffset + i];
            }
            return System.Text.Encoding.ASCII.GetString(extBytes);
        }
        set {
            // Ensure exactly 3 characters, space-padded if necessary
            string paddedExt = value.Length >= 3 ? value[..3] : value.PadRight(3, ' ');
            byte[] extBytes = System.Text.Encoding.ASCII.GetBytes(paddedExt);
            for (int i = 0; i < 3; i++) {
                UInt8[SearchExtOffset + i] = extBytes[i];
            }
        }
    }

    /// <summary>
    /// Sets up the DTA for a search operation.
    /// </summary>
    /// <param name="drive">The drive number to search on.</param>
    /// <param name="attributes">The search attributes.</param>
    /// <param name="pattern">The search pattern.</param>
    public void SetupSearch(byte drive, DosFileAttributes attributes, string pattern) {
        Drive = drive;
        SearchAttributes = (byte)attributes;

        // Parse pattern into name and extension
        string name;
        string ext = "";
        
        int dotPos = pattern.IndexOf('.');
        if (dotPos >= 0) {
            name = pattern[..dotPos];
            if (dotPos + 1 < pattern.Length) {
                ext = pattern[(dotPos + 1)..];
            }
        } else {
            name = pattern;
        }

        // Set name and extension in the DTA
        SearchName = name;
        SearchExtension = ext;
    }

    /// <summary>
    /// Gets the search parameters from the DTA.
    /// </summary>
    /// <param name="attributes">Output parameter for the search attributes.</param>
    /// <param name="pattern">Output parameter for the search pattern.</param>
    public void GetSearchParams(out DosFileAttributes attributes, out string pattern) {
        attributes = (DosFileAttributes)SearchAttributes;
        
        string name = SearchName.TrimEnd();
        string ext = SearchExtension.TrimEnd();
        
        pattern = name;
        if (!string.IsNullOrEmpty(ext)) {
            pattern += "." + ext;
        }
    }

    /// <summary>
    /// Gets the search result from the DTA.
    /// </summary>
    /// <returns>A DosSearchResult containing the search data.</returns>
    public DosSearchResult GetSearchResult() {
        return new DosSearchResult(FileName, FileSize, FileDate, FileTime, (DosFileAttributes)FileAttributes);
    }

    /// <summary>
    /// Sets the result of a search operation in the DTA.
    /// </summary>
    /// <param name="name">The file name.</param>
    /// <param name="size">The file size.</param>
    /// <param name="date">The file date.</param>
    /// <param name="time">The file time.</param>
    /// <param name="attributes">The file attributes.</param>
    public void SetResult(string name, uint size, ushort date, ushort time, DosFileAttributes attributes) {
        FileName = name;
        FileSize = size;
        FileDate = date;
        FileTime = time;
        FileAttributes = (byte)attributes;
    }
}