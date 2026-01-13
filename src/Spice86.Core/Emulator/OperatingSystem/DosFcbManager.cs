namespace Spice86.Core.Emulator.OperatingSystem;

using Serilog.Events;

using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Interfaces;

using System.Text;

/// <summary>
/// Implements DOS FCB (File Control Block) file operations.
/// These are CP/M-style file operations that were kept for backwards compatibility in DOS.
/// </summary>
/// <remarks>
/// <para>
/// FCB functions are considered legacy and were replaced by handle-based functions in DOS 2.0+.
/// However, many old games and programs still use them.
/// </para>
/// <para>
/// Supported INT 21h functions:
/// <list type="bullet">
///   <item>0x0F - Open File Using FCB</item>
///   <item>0x10 - Close File Using FCB</item>
///   <item>0x11 - Find First Using FCB</item>
///   <item>0x12 - Find Next Using FCB</item>
///   <item>0x13 - Delete File Using FCB</item>
///   <item>0x14 - Sequential Read Using FCB</item>
///   <item>0x15 - Sequential Write Using FCB</item>
///   <item>0x16 - Create File Using FCB</item>
///   <item>0x17 - Rename File Using FCB</item>
///   <item>0x21 - Random Read Using FCB</item>
///   <item>0x22 - Random Write Using FCB</item>
///   <item>0x23 - Get File Size Using FCB</item>
///   <item>0x24 - Set Random Record Number Using FCB</item>
///   <item>0x27 - Random Block Read Using FCB</item>
///   <item>0x28 - Random Block Write Using FCB</item>
///   <item>0x29 - Parse Filename into FCB</item>
/// </list>
/// </para>
/// <para>
/// Based on FreeDOS kernel implementation: https://github.com/FDOS/kernel/blob/master/kernel/fcbfns.c
/// </para>
/// </remarks>
public class DosFcbManager {
    /// <summary>
    /// FCB operation success code.
    /// </summary>
    public const byte FcbSuccess = 0x00;

    /// <summary>
    /// FCB operation error code (file not found, etc.).
    /// </summary>
    public const byte FcbError = 0xFF;

    /// <summary>
    /// FCB error code for no more data.
    /// </summary>
    public const byte FcbErrorNoData = 0x01;

    /// <summary>
    /// FCB error code for segment wrap.
    /// </summary>
    public const byte FcbErrorSegmentWrap = 0x02;

    /// <summary>
    /// FCB error code for end of file.
    /// </summary>
    public const byte FcbErrorEof = 0x03;

    private readonly IMemory _memory;
    private readonly DosFileManager _dosFileManager;
    private readonly DosDriveManager _dosDriveManager;
    private readonly ILoggerService _loggerService;
    private readonly DosPathResolver _dosPathResolver;

    /// <summary>
    /// Initializes a new instance of the <see cref="DosFcbManager"/> class.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="dosFileManager">The DOS file manager for handle-based operations.</param>
    /// <param name="dosDriveManager">The DOS drive manager.</param>
    /// <param name="loggerService">The logger service.</param>
    public DosFcbManager(IMemory memory, DosFileManager dosFileManager,
        DosDriveManager dosDriveManager, ILoggerService loggerService) {
        _memory = memory;
        _dosFileManager = dosFileManager;
        _dosDriveManager = dosDriveManager;
        _loggerService = loggerService;
        _dosPathResolver = new DosPathResolver(dosDriveManager);
    }

    /// <summary>
    /// Gets the FCB from the given address, handling both standard and extended FCBs.
    /// </summary>
    /// <param name="fcbAddress">The address of the FCB or extended FCB.</param>
    /// <param name="attribute">Output: the attribute from extended FCB, or 0 for standard FCB.</param>
    /// <returns>The standard FCB structure.</returns>
    public DosFileControlBlock GetFcb(uint fcbAddress, out byte attribute) {
        byte firstByte = _memory.UInt8[fcbAddress];
        if (firstByte == DosExtendedFileControlBlock.ExtendedFcbFlag) {
            DosExtendedFileControlBlock xfcb = new(_memory, fcbAddress);
            attribute = xfcb.Attribute;
            return xfcb.Fcb;
        }

        attribute = 0;
        return new DosFileControlBlock(_memory, fcbAddress);
    }

    /// <summary>
    /// Converts FCB file name format to a DOS path string.
    /// </summary>
    /// <param name="fcb">The FCB containing the file name.</param>
    /// <returns>A DOS file path string (e.g., "A:FILENAME.EXT").</returns>
    public string FcbToPath(DosFileControlBlock fcb) {
        StringBuilder path = new();

        // Add drive letter if specified
        byte drive = fcb.DriveNumber;
        if (drive == 0) {
            drive = (byte)(_dosDriveManager.CurrentDriveIndex + 1);
        }
        path.Append((char)('A' + drive - 1));
        path.Append(':');

        // Add file name (trimmed of spaces)
        string name = fcb.FileName.TrimEnd();
        path.Append(name);

        // Add extension if present
        string ext = fcb.FileExtension.TrimEnd();
        if (!string.IsNullOrEmpty(ext)) {
            path.Append('.');
            path.Append(ext);
        }

        return path.ToString();
    }

    /// <summary>
    /// INT 21h, AH=0Fh - Open File Using FCB.
    /// </summary>
    /// <param name="fcbAddress">The address of the FCB.</param>
    /// <returns>0x00 on success, 0xFF on failure.</returns>
    public byte OpenFile(uint fcbAddress) {
        DosFileControlBlock fcb = GetFcb(fcbAddress, out byte attribute);
        string dosPath = FcbToPath(fcb);

        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("FCB Open File: {Path}", dosPath);
        }

        string? hostPath = _dosPathResolver.GetFullHostPathFromDosOrDefault(dosPath);
        if (hostPath == null || !File.Exists(hostPath)) {
            return FcbError;
        }

        try {
            FileInfo fileInfo = new(hostPath);

            // Initialize FCB fields per FreeDOS behavior
            if (fcb.DriveNumber == 0) {
                fcb.DriveNumber = (byte)(_dosDriveManager.CurrentDriveIndex + 1);
            }
            fcb.CurrentBlock = 0;
            fcb.CurrentRecord = 0;
            fcb.RecordSize = DosFileControlBlock.DefaultRecordSize;
            fcb.FileSize = (uint)fileInfo.Length;
            fcb.Date = ToDosDate(fileInfo.LastWriteTime);
            fcb.Time = ToDosTime(fileInfo.LastWriteTime);

            // Use the DosFileManager to open the file and get a handle
            DosFileOperationResult result = _dosFileManager.OpenFileOrDevice(dosPath, FileAccessMode.ReadWrite);
            if (result.IsError) {
                // Try read-only
                result = _dosFileManager.OpenFileOrDevice(dosPath, FileAccessMode.ReadOnly);
                if (result.IsError) {
                    return FcbError;
                }
            }

            // Store the SFT number in the FCB
            fcb.SftNumber = (byte)(result.Value ?? 0xFF);

            return FcbSuccess;
        } catch (IOException ex) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning(ex, "FCB Open File failed: {Path}", dosPath);
            }
            return FcbError;
        }
    }

    /// <summary>
    /// INT 21h, AH=10h - Close File Using FCB.
    /// </summary>
    /// <param name="fcbAddress">The address of the FCB.</param>
    /// <returns>0x00 on success, 0xFF on failure.</returns>
    public byte CloseFile(uint fcbAddress) {
        DosFileControlBlock fcb = GetFcb(fcbAddress, out _);

        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("FCB Close File: SFT={SftNumber}", fcb.SftNumber);
        }

        // Already closed?
        if (fcb.SftNumber == 0xFF) {
            return FcbSuccess;
        }

        DosFileOperationResult result = _dosFileManager.CloseFileOrDevice(fcb.SftNumber);
        if (result.IsError) {
            return FcbError;
        }

        fcb.SftNumber = 0xFF;
        return FcbSuccess;
    }

    /// <summary>
    /// INT 21h, AH=16h - Create File Using FCB.
    /// </summary>
    /// <param name="fcbAddress">The address of the FCB.</param>
    /// <returns>0x00 on success, 0xFF on failure.</returns>
    public byte CreateFile(uint fcbAddress) {
        DosFileControlBlock fcb = GetFcb(fcbAddress, out byte attribute);
        string dosPath = FcbToPath(fcb);

        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("FCB Create File: {Path} with attribute {Attribute}",
                dosPath, attribute);
        }

        DosFileOperationResult result = _dosFileManager.CreateFileUsingHandle(dosPath, attribute);
        if (result.IsError) {
            return FcbError;
        }

        // Initialize FCB fields
        if (fcb.DriveNumber == 0) {
            fcb.DriveNumber = (byte)(_dosDriveManager.CurrentDriveIndex + 1);
        }
        fcb.CurrentBlock = 0;
        fcb.CurrentRecord = 0;
        fcb.RecordSize = DosFileControlBlock.DefaultRecordSize;
        fcb.FileSize = 0;
        fcb.Date = ToDosDate(DateTime.Now);
        fcb.Time = ToDosTime(DateTime.Now);
        fcb.SftNumber = (byte)(result.Value ?? 0xFF);

        return FcbSuccess;
    }

    /// <summary>
    /// INT 21h, AH=14h - Sequential Read Using FCB.
    /// </summary>
    /// <param name="fcbAddress">The address of the FCB.</param>
    /// <param name="dtaAddress">The address of the Disk Transfer Area.</param>
    /// <returns>0x00 on success, 0x01 if EOF reached before reading any data,
    /// 0x02 if segment wrap, 0x03 if EOF after partial read.</returns>
    public byte SequentialRead(uint fcbAddress, uint dtaAddress) {
        DosFileControlBlock fcb = GetFcb(fcbAddress, out _);
        return ReadWrite(fcb, dtaAddress, 1, isRead: true, isRandom: false);
    }

    /// <summary>
    /// INT 21h, AH=15h - Sequential Write Using FCB.
    /// </summary>
    /// <param name="fcbAddress">The address of the FCB.</param>
    /// <param name="dtaAddress">The address of the Disk Transfer Area.</param>
    /// <returns>0x00 on success, 0x01 if disk full, 0x02 if segment wrap.</returns>
    public byte SequentialWrite(uint fcbAddress, uint dtaAddress) {
        DosFileControlBlock fcb = GetFcb(fcbAddress, out _);
        return ReadWrite(fcb, dtaAddress, 1, isRead: false, isRandom: false);
    }

    /// <summary>
    /// INT 21h, AH=21h - Random Read Using FCB.
    /// </summary>
    /// <param name="fcbAddress">The address of the FCB.</param>
    /// <param name="dtaAddress">The address of the Disk Transfer Area.</param>
    /// <returns>0x00 on success, 0x01 if EOF, 0x02 if segment wrap, 0x03 if partial read.</returns>
    public byte RandomRead(uint fcbAddress, uint dtaAddress) {
        DosFileControlBlock fcb = GetFcb(fcbAddress, out _);
        fcb.CalculateRecordPosition();
        return ReadWrite(fcb, dtaAddress, 1, isRead: true, isRandom: true);
    }

    /// <summary>
    /// INT 21h, AH=22h - Random Write Using FCB.
    /// </summary>
    /// <param name="fcbAddress">The address of the FCB.</param>
    /// <param name="dtaAddress">The address of the Disk Transfer Area.</param>
    /// <returns>0x00 on success, 0x01 if disk full, 0x02 if segment wrap.</returns>
    public byte RandomWrite(uint fcbAddress, uint dtaAddress) {
        DosFileControlBlock fcb = GetFcb(fcbAddress, out _);
        fcb.CalculateRecordPosition();
        return ReadWrite(fcb, dtaAddress, 1, isRead: false, isRandom: true);
    }

    /// <summary>
    /// INT 21h, AH=27h - Random Block Read Using FCB.
    /// </summary>
    /// <param name="fcbAddress">The address of the FCB.</param>
    /// <param name="dtaAddress">The address of the Disk Transfer Area.</param>
    /// <param name="recordCount">Number of records to read (in/out).</param>
    /// <returns>0x00 on success, error code otherwise.</returns>
    public byte RandomBlockRead(uint fcbAddress, uint dtaAddress, ref ushort recordCount) {
        DosFileControlBlock fcb = GetFcb(fcbAddress, out _);
        fcb.CalculateRecordPosition();

        uint oldRandom = fcb.RandomRecord;
        byte result = ReadWrite(fcb, dtaAddress, recordCount, isRead: true, isRandom: true);
        recordCount = (ushort)(fcb.RandomRecord - oldRandom);
        fcb.CalculateRecordPosition();

        return result;
    }

    /// <summary>
    /// INT 21h, AH=28h - Random Block Write Using FCB.
    /// </summary>
    /// <param name="fcbAddress">The address of the FCB.</param>
    /// <param name="dtaAddress">The address of the Disk Transfer Area.</param>
    /// <param name="recordCount">Number of records to write (in/out).</param>
    /// <returns>0x00 on success, error code otherwise.</returns>
    public byte RandomBlockWrite(uint fcbAddress, uint dtaAddress, ref ushort recordCount) {
        DosFileControlBlock fcb = GetFcb(fcbAddress, out _);
        fcb.CalculateRecordPosition();

        // Special case: record count of 0 truncates file
        if (recordCount == 0) {
            return TruncateFile(fcb);
        }

        uint oldRandom = fcb.RandomRecord;
        byte result = ReadWrite(fcb, dtaAddress, recordCount, isRead: false, isRandom: true);
        recordCount = (ushort)(fcb.RandomRecord - oldRandom);
        fcb.CalculateRecordPosition();

        return result;
    }

    /// <summary>
    /// INT 21h, AH=23h - Get File Size Using FCB.
    /// </summary>
    /// <param name="fcbAddress">The address of the FCB.</param>
    /// <returns>0x00 on success, 0xFF if file not found.</returns>
    public byte GetFileSize(uint fcbAddress) {
        DosFileControlBlock fcb = GetFcb(fcbAddress, out _);
        string dosPath = FcbToPath(fcb);

        if (fcb.RecordSize == 0) {
            return FcbError;
        }

        string? hostPath = _dosPathResolver.GetFullHostPathFromDosOrDefault(dosPath);
        if (hostPath == null || !File.Exists(hostPath)) {
            return FcbError;
        }

        try {
            FileInfo fileInfo = new(hostPath);
            uint fileSize = (uint)fileInfo.Length;
            uint recordSize = fcb.RecordSize;

            // Set random record to the number of records (rounded up)
            fcb.RandomRecord = (fileSize + recordSize - 1) / recordSize;

            return FcbSuccess;
        } catch (IOException) {
            return FcbError;
        }
    }

    /// <summary>
    /// INT 21h, AH=24h - Set Random Record Number Using FCB.
    /// </summary>
    /// <param name="fcbAddress">The address of the FCB.</param>
    public void SetRandomRecordNumber(uint fcbAddress) {
        DosFileControlBlock fcb = GetFcb(fcbAddress, out _);
        fcb.SetRandomFromPosition();
    }

    /// <summary>
    /// INT 21h, AH=29h - Parse Filename into FCB.
    /// </summary>
    /// <param name="stringAddress">The address of the filename string to parse.</param>
    /// <param name="fcbAddress">The address of the FCB to fill.</param>
    /// <param name="parseControl">Parsing control byte.</param>
    /// <returns>0x00 if no wildcards, 0x01 if wildcards present, 0xFF if invalid drive.</returns>
    public byte ParseFilename(uint stringAddress, uint fcbAddress, byte parseControl) {
        DosFileControlBlock fcb = GetFcb(fcbAddress, out _);

        // Read the filename string from memory
        string filename = _memory.GetZeroTerminatedString(stringAddress, 128);
        int pos = 0;

        bool skipLeadingSeparators = (parseControl & 0x01) != 0;
        bool setDefaultDrive = (parseControl & 0x02) == 0;
        bool blankFileName = (parseControl & 0x04) == 0;
        bool blankExtension = (parseControl & 0x08) == 0;

        // Skip leading separators if requested
        if (skipLeadingSeparators) {
            while (pos < filename.Length && IsParseCommonSeparator(filename[pos])) {
                pos++;
            }
        }

        // Skip whitespace
        while (pos < filename.Length && char.IsWhiteSpace(filename[pos])) {
            pos++;
        }

        bool hasWildcard = false;
        bool invalidDrive = false;

        // Check for drive specification
        if (pos + 1 < filename.Length && filename[pos + 1] == ':') {
            char driveChar = char.ToUpper(filename[pos]);
            if (driveChar >= 'A' && driveChar <= 'Z') {
                byte driveNum = (byte)(driveChar - 'A' + 1);
                if (!_dosDriveManager.HasDriveAtIndex((byte)(driveNum - 1))) {
                    invalidDrive = true;
                }
                fcb.DriveNumber = driveNum;
                pos += 2;
            }
        } else if (setDefaultDrive) {
            fcb.DriveNumber = 0; // Default drive
        }

        // Clear fields if requested
        if (blankFileName) {
            fcb.FileName = "        ";
        }
        if (blankExtension) {
            fcb.FileExtension = "   ";
        }

        // Special case: "." and ".."
        if (pos < filename.Length && filename[pos] == '.') {
            char[] nameChars = "        ".ToCharArray();
            nameChars[0] = '.';
            pos++;
            if (pos < filename.Length && filename[pos] == '.') {
                nameChars[1] = '.';
            }
            fcb.FileName = new string(nameChars);
            return invalidDrive ? FcbError : FcbSuccess;
        }

        // Parse file name (up to 8 characters)
        StringBuilder name = new();
        while (pos < filename.Length && !IsParseFieldSeparator(filename[pos]) && name.Length < 8) {
            char c = filename[pos];
            if (c == '*') {
                hasWildcard = true;
                while (name.Length < 8) name.Append('?');
                break;
            }
            if (c == '?') {
                hasWildcard = true;
            }
            name.Append(char.ToUpper(c));
            pos++;
        }

        // Skip remaining name characters if over 8
        while (pos < filename.Length && !IsParseFieldSeparator(filename[pos])) {
            pos++;
        }

        if (name.Length > 0) {
            fcb.FileName = name.ToString().PadRight(8);
        }

        // Parse extension if present
        if (pos < filename.Length && filename[pos] == '.') {
            pos++;
            StringBuilder ext = new();
            while (pos < filename.Length && !IsParseFieldSeparator(filename[pos]) && ext.Length < 3) {
                char c = filename[pos];
                if (c == '*') {
                    hasWildcard = true;
                    while (ext.Length < 3) ext.Append('?');
                    break;
                }
                if (c == '?') {
                    hasWildcard = true;
                }
                ext.Append(char.ToUpper(c));
                pos++;
            }

            if (ext.Length > 0) {
                fcb.FileExtension = ext.ToString().PadRight(3);
            }
        }

        if (invalidDrive) {
            return FcbError;
        }
        return hasWildcard ? (byte)0x01 : FcbSuccess;
    }

    /// <summary>
    /// Performs FCB read/write operation.
    /// </summary>
    private byte ReadWrite(DosFileControlBlock fcb, uint dtaAddress, ushort recordCount, bool isRead, bool isRandom) {
        ushort recordSize = fcb.RecordSize;
        if (recordSize == 0) {
            recordSize = DosFileControlBlock.DefaultRecordSize;
        }

        uint totalSize = (uint)recordSize * recordCount;

        // Check for segment wrap
        ushort dtaOffset = (ushort)(dtaAddress & 0xFFFF);
        if (dtaOffset + totalSize < dtaOffset) {
            return FcbErrorSegmentWrap;
        }

        // Calculate file position
        long position = (long)fcb.AbsoluteRecord * recordSize;

        // Get the open file
        VirtualFileBase? file = GetOpenFcbFile(fcb.SftNumber);
        if (file == null || !file.CanSeek) {
            return FcbErrorNoData;
        }

        try {
            file.Seek(position, SeekOrigin.Begin);

            if (isRead) {
                byte[] buffer = new byte[totalSize];
                int bytesRead = file.Read(buffer, 0, (int)totalSize);

                if (bytesRead == 0) {
                    return FcbErrorNoData;
                }

                // Write to DTA
                for (int i = 0; i < bytesRead; i++) {
                    _memory.UInt8[dtaAddress + (uint)i] = buffer[i];
                }

                // Pad with zeros if partial read
                if (bytesRead < totalSize) {
                    for (uint i = (uint)bytesRead; i < totalSize; i++) {
                        _memory.UInt8[dtaAddress + i] = 0;
                    }
                }

                // Update FCB position
                if (isRandom) {
                    fcb.RandomRecord += (uint)((bytesRead + recordSize - 1) / recordSize);
                } else {
                    fcb.NextRecord();
                }

                if (bytesRead < totalSize) {
                    return FcbErrorEof;
                }

                return FcbSuccess;
            } else {
                // Write operation
                byte[] buffer = new byte[totalSize];
                for (uint i = 0; i < totalSize; i++) {
                    buffer[i] = _memory.UInt8[dtaAddress + i];
                }

                file.Write(buffer, 0, (int)totalSize);

                // Update file size in FCB
                long newSize = file.Position;
                if (newSize > fcb.FileSize) {
                    fcb.FileSize = (uint)newSize;
                }

                if (isRandom) {
                    fcb.RandomRecord += recordCount;
                } else {
                    fcb.NextRecord();
                }

                return FcbSuccess;
            }
        } catch (IOException) {
            return FcbErrorNoData;
        }
    }

    /// <summary>
    /// Truncates a file to the current position.
    /// </summary>
    private byte TruncateFile(DosFileControlBlock fcb) {
        VirtualFileBase? file = GetOpenFcbFile(fcb.SftNumber);
        if (file == null || !file.CanSeek) {
            return FcbErrorNoData;
        }

        try {
            long position = (long)fcb.AbsoluteRecord * fcb.RecordSize;
            file.SetLength(position);
            fcb.FileSize = (uint)position;
            return FcbSuccess;
        } catch (IOException) {
            return FcbErrorNoData;
        }
    }

    /// <summary>
    /// Gets the open file for an FCB operation.
    /// </summary>
    private VirtualFileBase? GetOpenFcbFile(byte sftNumber) {
        if (sftNumber == 0xFF || sftNumber >= _dosFileManager.OpenFiles.Length) {
            return null;
        }
        return _dosFileManager.OpenFiles[sftNumber];
    }

    /// <summary>
    /// Checks if a character is a common FCB separator.
    /// </summary>
    private static bool IsParseCommonSeparator(char c) {
        return ":;,=+ \t".Contains(c);
    }

    /// <summary>
    /// Checks if a character is a field separator for FCB parsing.
    /// </summary>
    private static bool IsParseFieldSeparator(char c) {
        return c <= ' ' || "/\\\"[]<>|.:;,=+\t".Contains(c);
    }

    /// <summary>
    /// Converts a DateTime to DOS date format.
    /// </summary>
    private static ushort ToDosDate(DateTime date) {
        int day = date.Day;
        int month = date.Month;
        int dosYear = date.Year - 1980;
        return (ushort)((day & 0x1F) | ((month & 0x0F) << 5) | ((dosYear & 0x7F) << 9));
    }

    /// <summary>
    /// Converts a DateTime to DOS time format.
    /// </summary>
    private static ushort ToDosTime(DateTime time) {
        int seconds = time.Second / 2;
        int minutes = time.Minute;
        int hours = time.Hour;
        return (ushort)((seconds & 0x1F) | ((minutes & 0x3F) << 5) | ((hours & 0x1F) << 11));
    }

    /// <summary>
    /// Offset of the reserved area in the FCB structure, used for storing search state.
    /// </summary>
    private const uint FcbReservedAreaOffset = 0x18;

    /// <summary>
    /// Counter for generating unique search IDs for FCB file searches.
    /// </summary>
    private uint _fcbSearchIdCounter;

    /// <summary>
    /// Tracks active FCB file searches. Key is the search ID stored in the FCB reserved area.
    /// </summary>
    private readonly Dictionary<uint, FcbSearchData> _fcbActiveSearches = new();

    /// <summary>
    /// Stores search state for FCB Find First/Next operations.
    /// </summary>
    /// <remarks>
    /// The matching files list is cached during FindFirst to ensure consistent
    /// results across FindNext calls, per DOS semantics.
    /// </remarks>
    private class FcbSearchData {
        public FcbSearchData(string[] matchingFiles, int index, byte searchAttribute, byte driveNumber, bool isExtended) {
            MatchingFiles = matchingFiles;
            Index = index;
            SearchAttribute = searchAttribute;
            DriveNumber = driveNumber;
            IsExtended = isExtended;
        }

        public string[] MatchingFiles { get; init; }
        public int Index { get; set; }
        public byte SearchAttribute { get; init; }
        public byte DriveNumber { get; init; }
        public bool IsExtended { get; init; }
    }

    /// <summary>
    /// INT 21h, AH=11h - Find First Matching File Using FCB.
    /// </summary>
    /// <param name="fcbAddress">The address of the FCB.</param>
    /// <param name="dtaAddress">The address of the Disk Transfer Area.</param>
    /// <returns>0x00 if a matching file was found (DTA is filled), 0xFF if no match was found.</returns>
    public byte FindFirst(uint fcbAddress, uint dtaAddress) {
        DosFileControlBlock fcb = GetFcb(fcbAddress, out byte searchAttribute);
        bool isExtended = _memory.UInt8[fcbAddress] == DosExtendedFileControlBlock.ExtendedFcbFlag;

        string dosPath = FcbToPath(fcb);

        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("FCB Find First: {Path}, Attribute: {Attribute}, Extended: {Extended}",
                dosPath, searchAttribute, isExtended);
        }

        // Get drive number
        byte driveNumber = fcb.DriveNumber;
        if (driveNumber == 0) {
            driveNumber = (byte)(_dosDriveManager.CurrentDriveIndex + 1);
        }

        // Get the search folder and pattern from the FCB path
        string searchPattern = GetSearchPattern(fcb);
        string? searchFolder = GetSearchFolder(dosPath);

        if (searchFolder == null) {
            if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                _loggerService.Debug("FCB Find First: Search folder not found for path {Path}", dosPath);
            }
            return FcbError;
        }

        try {
            // Find matching files and cache them for subsequent FindNext calls
            EnumerationOptions options = GetEnumerationOptions(searchAttribute);
            string[] matchingFiles = FindFilesUsingWildCmp(searchFolder, searchPattern, options);

            if (matchingFiles.Length == 0) {
                if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                    _loggerService.Debug("FCB Find First: No matching files found in {Folder} for pattern {Pattern}",
                        searchFolder, searchPattern);
                }
                return FcbError;
            }

            // Fill the DTA with the first match
            if (!FillDtaWithMatch(dtaAddress, matchingFiles[0], searchFolder, driveNumber, isExtended)) {
                return FcbError;
            }

            // Store search state in FCB reserved area (per DOS semantics)
            uint searchId = GenerateSearchId();
            StoreFcbSearchState(fcbAddress, searchId, isExtended);
            _fcbActiveSearches[searchId] = new FcbSearchData(matchingFiles, 1, searchAttribute, driveNumber, isExtended);

            if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                _loggerService.Debug("FCB Find First: Found {File}, SearchId: {Id}", matchingFiles[0], searchId);
            }

            return FcbSuccess;
        } catch (IOException ex) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning(ex, "FCB Find First: IO error searching {Folder}", searchFolder);
            }
            return FcbError;
        } catch (UnauthorizedAccessException ex) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning(ex, "FCB Find First: Access denied searching {Folder}", searchFolder);
            }
            return FcbError;
        }
    }

    /// <summary>
    /// INT 21h, AH=12h - Find Next Matching File Using FCB.
    /// </summary>
    /// <param name="fcbAddress">The address of the FCB (same FCB used for Find First).</param>
    /// <param name="dtaAddress">The address of the Disk Transfer Area.</param>
    /// <returns>0x00 if a matching file was found (DTA is filled), 0xFF if no more files match.</returns>
    public byte FindNext(uint fcbAddress, uint dtaAddress) {
        bool isExtended = _memory.UInt8[fcbAddress] == DosExtendedFileControlBlock.ExtendedFcbFlag;

        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("FCB Find Next, Extended: {Extended}", isExtended);
        }

        // Get search ID from FCB reserved area (per DOS semantics)
        uint searchId = GetFcbSearchState(fcbAddress, isExtended);

        if (!_fcbActiveSearches.TryGetValue(searchId, out FcbSearchData? searchData)) {
            if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                _loggerService.Debug("FCB Find Next: No active search found for ID {Id}", searchId);
            }
            return FcbError;
        }

        // Use cached file list from FindFirst
        if (searchData.Index >= searchData.MatchingFiles.Length) {
            if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                _loggerService.Debug("FCB Find Next: No more matching files (index {Index}, total {Total})",
                    searchData.Index, searchData.MatchingFiles.Length);
            }
            // Clean up exhausted search to prevent memory leak
            _fcbActiveSearches.Remove(searchId);
            return FcbError;
        }

        try {
            // Fill the DTA with the next match from cached list
            string matchingFile = searchData.MatchingFiles[searchData.Index];
            string? searchFolder = Path.GetDirectoryName(matchingFile);
            if (searchFolder == null || !FillDtaWithMatch(dtaAddress, matchingFile, searchFolder, searchData.DriveNumber, searchData.IsExtended)) {
                return FcbError;
            }

            // Update search state in FCB
            searchData.Index++;
            StoreFcbSearchState(fcbAddress, searchId, isExtended);

            if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                _loggerService.Debug("FCB Find Next: Found {File}", matchingFile);
            }

            return FcbSuccess;
        } catch (IOException ex) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning(ex, "FCB Find Next: IO error");
            }
            return FcbError;
        } catch (UnauthorizedAccessException ex) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning(ex, "FCB Find Next: Access denied");
            }
            return FcbError;
        }
    }

    /// <summary>
    /// Gets the search pattern from the FCB (filename with possible wildcards).
    /// </summary>
    private static string GetSearchPattern(DosFileControlBlock fcb) {
        string name = fcb.FileName.TrimEnd();
        string ext = fcb.FileExtension.TrimEnd();

        // Convert FCB wildcards (?) to search pattern
        if (string.IsNullOrEmpty(ext)) {
            return name;
        }
        return $"{name}.{ext}";
    }

    /// <summary>
    /// Gets the search folder from a DOS path.
    /// </summary>
    private string? GetSearchFolder(string dosPath) {
        // Extract directory portion from path
        int lastSep = dosPath.LastIndexOfAny(new[] { '\\', '/' });
        string directory;
        if (lastSep >= 0) {
            directory = dosPath[..(lastSep + 1)];
        } else {
            // Just a filename, search in current directory
            int colonPos = dosPath.IndexOf(':');
            if (colonPos >= 0) {
                directory = dosPath[..(colonPos + 1)];
            } else {
                directory = ".";
            }
        }

        return _dosPathResolver.GetFullHostPathFromDosOrDefault(directory);
    }

    /// <summary>
    /// Gets enumeration options based on search attributes.
    /// </summary>
    /// <remarks>
    /// When attributes is 0 (normal files only), Hidden, System, and Directory files are excluded.
    /// When specific attribute flags are set, those file types are included in addition to normal files.
    /// </remarks>
    private static EnumerationOptions GetEnumerationOptions(byte attributes) {
        EnumerationOptions options = new() {
            IgnoreInaccessible = true,
            RecurseSubdirectories = false,
            MatchCasing = MatchCasing.CaseInsensitive,
            MatchType = MatchType.Win32
        };

        DosFileAttributes dosAttribs = (DosFileAttributes)attributes;
        
        // By default, skip special files (hidden, system, directory)
        // Only include them if the corresponding attribute flag is explicitly set
        FileAttributes skip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.Directory;

        // Include directories if the Directory attribute is set
        if (dosAttribs.HasFlag(DosFileAttributes.Directory)) {
            skip &= ~FileAttributes.Directory;
        }
        // Include hidden files if the Hidden attribute is set
        if (dosAttribs.HasFlag(DosFileAttributes.Hidden)) {
            skip &= ~FileAttributes.Hidden;
        }
        // Include system files if the System attribute is set
        if (dosAttribs.HasFlag(DosFileAttributes.System)) {
            skip &= ~FileAttributes.System;
        }

        options.AttributesToSkip = skip;
        return options;
    }

    /// <summary>
    /// Finds files matching a wildcard pattern.
    /// </summary>
    private string[] FindFilesUsingWildCmp(string searchFolder, string searchPattern, EnumerationOptions options) {
        List<string> results = new();
        foreach (string path in Directory.EnumerateFileSystemEntries(searchFolder, "*", options)) {
            if (DosPathResolver.WildFileCmp(Path.GetFileName(path), searchPattern)) {
                results.Add(path);
            }
        }
        return results.ToArray();
    }

    /// <summary>
    /// Fills the DTA with a matching file entry in FCB format.
    /// </summary>
    /// <remarks>
    /// <para>The DTA for FCB operations is structured as follows:</para>
    /// <para>For regular FCB (37 bytes):</para>
    /// <list type="bullet">
    ///   <item>Offset 0x00 (1 byte): Drive number</item>
    ///   <item>Offset 0x01 (8 bytes): File name (space-padded)</item>
    ///   <item>Offset 0x09 (3 bytes): File extension (space-padded)</item>
    ///   <item>Offset 0x0C (2 bytes): Current block (0)</item>
    ///   <item>Offset 0x0E (2 bytes): Record size (128)</item>
    ///   <item>Offset 0x10 (4 bytes): File size</item>
    ///   <item>Offset 0x14 (2 bytes): Date</item>
    ///   <item>Offset 0x16 (2 bytes): Time</item>
    ///   <item>Offset 0x18 (8 bytes): Reserved/system use</item>
    ///   <item>Offset 0x20 (1 byte): Current record (0)</item>
    ///   <item>Offset 0x21 (4 bytes): Random record (0)</item>
    /// </list>
    /// <para>For extended FCB (44 bytes = 7 byte header + 37 byte FCB):</para>
    /// <list type="bullet">
    ///   <item>Offset 0x00 (1 byte): 0xFF flag</item>
    ///   <item>Offset 0x01 (5 bytes): Reserved</item>
    ///   <item>Offset 0x06 (1 byte): File attributes</item>
    ///   <item>Offset 0x07 (37 bytes): Regular FCB structure</item>
    /// </list>
    /// </remarks>
    private bool FillDtaWithMatch(uint dtaAddress, string matchingFile, string searchFolder, byte driveNumber, bool isExtended) {
        try {
            FileSystemInfo entryInfo = Directory.Exists(matchingFile)
                ? new DirectoryInfo(matchingFile)
                : new FileInfo(matchingFile);

            string fileName = Path.GetFileName(matchingFile);
            string shortName = DosPathResolver.GetShortFileName(fileName, searchFolder);

            // Parse the short name into FCB format (8.3)
            string name;
            string ext;
            int dotPos = shortName.LastIndexOf('.');
            if (dotPos >= 0) {
                name = shortName[..dotPos].PadRight(DosFileControlBlock.FileNameSize);
                ext = shortName[(dotPos + 1)..].PadRight(DosFileControlBlock.FileExtensionSize);
            } else {
                name = shortName.PadRight(DosFileControlBlock.FileNameSize);
                ext = "   ";
            }

            // Truncate if too long
            if (name.Length > DosFileControlBlock.FileNameSize) {
                name = name[..DosFileControlBlock.FileNameSize];
            }
            if (ext.Length > DosFileControlBlock.FileExtensionSize) {
                ext = ext[..DosFileControlBlock.FileExtensionSize];
            }

            uint fcbOffset = 0;

            // For extended FCB, write the extended header first using the structure class
            if (isExtended) {
                DosExtendedFileControlBlock xfcb = new(_memory, dtaAddress);
                xfcb.Flag = DosExtendedFileControlBlock.ExtendedFcbFlag;
                xfcb.Attribute = (byte)ConvertToDosFileAttributes(entryInfo.Attributes);
                fcbOffset = DosExtendedFileControlBlock.HeaderSize;
            }

            // Use DosFileControlBlock structure to write the DTA entry
            DosFileControlBlock dtaFcb = new(_memory, dtaAddress + fcbOffset);
            dtaFcb.DriveNumber = driveNumber;
            dtaFcb.FileName = name.ToUpperInvariant();
            dtaFcb.FileExtension = ext.ToUpperInvariant();
            dtaFcb.CurrentBlock = 0;
            dtaFcb.RecordSize = DosFileControlBlock.DefaultRecordSize;
            dtaFcb.FileSize = entryInfo is FileInfo fi ? (uint)fi.Length : 0;
            dtaFcb.Date = ToDosDate(entryInfo.LastWriteTime);
            dtaFcb.Time = ToDosTime(entryInfo.LastWriteTime);
            dtaFcb.CurrentRecord = 0;
            dtaFcb.RandomRecord = 0;

            return true;
        } catch (IOException ex) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning(ex, "FCB FillDtaWithMatch: Error getting file info for {File}", matchingFile);
            }
            return false;
        }
    }

    /// <summary>
    /// Generates a new search ID for tracking FCB searches.
    /// </summary>
    private uint GenerateSearchId() {
        return ++_fcbSearchIdCounter;
    }

    /// <summary>
    /// Stores the search ID in the FCB reserved area.
    /// </summary>
    private void StoreFcbSearchState(uint fcbAddress, uint searchId, bool isExtended) {
        // Store the search ID in the first 4 bytes of the FCB reserved area
        // For extended FCB, skip the 7-byte header
        uint reservedOffset = isExtended ? (uint)DosExtendedFileControlBlock.HeaderSize + FcbReservedAreaOffset : FcbReservedAreaOffset;
        _memory.UInt32[fcbAddress + reservedOffset] = searchId;
    }

    /// <summary>
    /// Gets the search ID from the FCB reserved area.
    /// </summary>
    private uint GetFcbSearchState(uint fcbAddress, bool isExtended) {
        uint reservedOffset = isExtended ? (uint)DosExtendedFileControlBlock.HeaderSize + FcbReservedAreaOffset : FcbReservedAreaOffset;
        return _memory.UInt32[fcbAddress + reservedOffset];
    }

    /// <summary>
    /// Converts .NET FileAttributes to DOS file attributes.
    /// </summary>
    /// <remarks>
    /// This explicit conversion is safer than direct casting as it doesn't rely on
    /// the underlying enum values matching between FileAttributes and DosFileAttributes.
    /// </remarks>
    private static DosFileAttributes ConvertToDosFileAttributes(FileAttributes attributes) {
        DosFileAttributes result = DosFileAttributes.Normal;
        if (attributes.HasFlag(FileAttributes.ReadOnly)) {
            result |= DosFileAttributes.ReadOnly;
        }
        if (attributes.HasFlag(FileAttributes.Hidden)) {
            result |= DosFileAttributes.Hidden;
        }
        if (attributes.HasFlag(FileAttributes.System)) {
            result |= DosFileAttributes.System;
        }
        if (attributes.HasFlag(FileAttributes.Directory)) {
            result |= DosFileAttributes.Directory;
        }
        if (attributes.HasFlag(FileAttributes.Archive)) {
            result |= DosFileAttributes.Archive;
        }
        return result;
    }
}
