namespace Spice86.Core.Emulator.OperatingSystem;

using System;
using System.Collections.Generic;
using System.Text;

using Serilog.Events;

using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

/// <summary>
/// FCB (File Control Block) manager aligned with FreeDOS <c>fcbfns.c</c> behavior.
/// </summary>
public class DosFcbManager {

    private const int RenameNewNameOffset = 0x0C;
    private const int RenameNewExtensionOffset = 0x14;

    // FreeDOS parse control constants
    public const byte PARSE_SKIP_LEAD_SEP = 0x01;
    public const byte PARSE_DFLT_DRIVE = 0x02;
    public const byte PARSE_BLNK_FNAME = 0x04;
    public const byte PARSE_BLNK_FEXT = 0x08;

    public const byte PARSE_RET_NOWILD = 0;
    public const byte PARSE_RET_WILD = 1;
    public const byte PARSE_RET_BADDRIVE = 0xFF;

    // FreeDOS separators
    public const string COMMON_SEPS = ":;,=+ \t";
    public const string FIELD_SEPS = "/\\\"[]<>|.:;,=+\t";

    private readonly IMemory _memory;
    private readonly DosFileManager _dosFileManager;
    private readonly DosDriveManager _dosDriveManager;
    private readonly ILoggerService _loggerService;
    private readonly HashSet<ushort> _trackedFcbHandles = new();

    public DosFcbManager(IMemory memory, DosFileManager dosFileManager, DosDriveManager dosDriveManager, ILoggerService loggerService) {
        _memory = memory;
        _dosFileManager = dosFileManager;
        _dosDriveManager = dosDriveManager;
        _loggerService = loggerService;
    }

    /// <summary>
    /// INT 21h, AH=29h - Parse filename into an FCB, matching FreeDOS parsing semantics.
    /// </summary>
    /// <param name="stringAddress">Linear address of the ASCIIZ string to parse.</param>
    /// <param name="fcbAddress">Linear address of the destination FCB (standard or extended).</param>
    /// <param name="parseControl">Parsing control flags (PARSE_*).</param>
    /// <param name="bytesAdvanced">Number of bytes consumed from the input string.</param>
    /// <returns>An <see cref="FcbParseResult"/> describing parse status.</returns>
    public FcbParseResult ParseFilename(uint stringAddress, uint fcbAddress, byte parseControl, out uint bytesAdvanced) {
        string filename = _memory.GetZeroTerminatedString(stringAddress, 128);
        DosFileControlBlock fcb = GetFcb(fcbAddress, out _);
        
        int pos = 0;
        bool retCodeDrive = false;
        bool retCodeName = false;
        bool retCodeExt = false;

        // Skip leading separators if requested
        // FreeDOS: "if (*wTestMode & PARSE_SKIP_LEAD_SEP)"
        if ((parseControl & PARSE_SKIP_LEAD_SEP) != 0) {
            while (pos < filename.Length && TestCmnSeps(filename[pos])) {
                pos++;
            }
        }

        // Skip whitespace (undocumented: "Undocumented 'feature,' we skip white space anyway")
        pos = ParseSkipWh(filename, pos);

        // Check for drive specification
        // FreeDOS: "if (!TestFieldSeps(lpFileName) && *(lpFileName + 1) == ':'"
        if (pos + 1 < filename.Length && filename[pos + 1] == ':' && !TestFieldSeps(filename[pos])) {
            char driveChar = char.ToUpper(filename[pos]);
            if (driveChar is >= 'A' and <= 'Z') {
                byte driveNum = (byte)(driveChar - 'A');
                
                // FreeDOS: "Undocumented behavior: should keep parsing even if drive is invalid"
                if (!_dosDriveManager.HasDriveAtIndex(driveNum)) {
                    retCodeDrive = true;
                }
                
                fcb.DriveNumber = (byte)(driveNum + 1);
                pos += 2;
            }
        } else if ((parseControl & PARSE_DFLT_DRIVE) == 0) {
            // FreeDOS: "} else if (!(*wTestMode & PARSE_DFLT_DRIVE)) {"
            // If flag NOT set, set to default drive (0)
            fcb.DriveNumber = 0;
        }

        /* Undocumented behavior, set record number & record size to 0  */
        /* per MS-DOS Encyclopedia pp269 no other FCB fields modified   */
        /* except zeroing current block and record size fields          */
        // FreeDOS: "lpFcb->fcb_cublock = lpFcb->fcb_recsiz = 0;"
        fcb.CurrentBlock = 0;
        fcb.RecordSize = 0;

        // Blank filename field if requested
        if ((parseControl & PARSE_BLNK_FNAME) == 0) {
            fcb.FileName = "        ";
        }
        
        // Blank extension field if requested
        if ((parseControl & PARSE_BLNK_FEXT) == 0) {
            fcb.FileExtension = "   ";
        }

        // Special cases: '.' and '..'
        // FreeDOS: "if (*lpFileName == '.')"
        if (pos < filename.Length && filename[pos] == '.') {
            StringBuilder nameBuilder = new("        ");
            nameBuilder[0] = '.';
            pos++;
            
            if (pos < filename.Length && filename[pos] == '.') {
                nameBuilder[1] = '.';
                pos++;
            }
            
            fcb.FileName = nameBuilder.ToString();
            bytesAdvanced = (uint)pos;
            return retCodeDrive ? FcbParseResult.InvalidDrive : FcbParseResult.NoWildcards;
        }

        // Parse filename field
        int nameStart = pos;
        (pos, retCodeName) = GetNameField(filename, pos, 8);
        fcb.FileName = ExtractAndPadField(filename, nameStart, pos, 8, retCodeName);

        // Parse extension if present
        if (pos < filename.Length && filename[pos] == '.') {
            pos++;
            int extStart = pos;
            (pos, retCodeExt) = GetNameField(filename, pos, 3);
            fcb.FileExtension = ExtractAndPadField(filename, extStart, pos, 3, retCodeExt);
        }

        bytesAdvanced = (uint)pos;

        // Return appropriate code
        if (retCodeDrive) {
            return FcbParseResult.InvalidDrive;
        }

        if (retCodeName || retCodeExt) {
            return FcbParseResult.WildcardsPresent;
        }

        return FcbParseResult.NoWildcards;
    }

    /// <summary>
    /// Checks if character is a common separator.
    /// FreeDOS: TestCmnSeps - ":;,=+ \t"
    /// </summary>
    private static bool TestCmnSeps(char c) {
        return COMMON_SEPS.Contains(c);
    }

    /// <summary>
    /// Checks if character is a field separator.
    /// FreeDOS: TestFieldSeps - (unsigned char)*lpFileName &lt;= ' ' || "/\\\"[]&lt;&gt;|.:;,=+\t"
    /// </summary>
    private static bool TestFieldSeps(char c) {
        return c <= ' ' || FIELD_SEPS.Contains(c);
    }

    /// <summary>
    /// Skip whitespace.
    /// FreeDOS: ParseSkipWh
    /// </summary>
    private static int ParseSkipWh(string filename, int pos) {
        while (pos < filename.Length && (filename[pos] == ' ' || filename[pos] == '\t')) {
            pos++;
        }
        return pos;
    }

    /// <summary>
    /// Parse a name field (filename or extension).
    /// FreeDOS: GetNameField.
    /// Returns a tuple of (newPos, hasWildcard).
    /// </summary>
    private (int newPos, bool hasWildcard) GetNameField(string filename, int pos, int fieldSize) {
        bool hasWildcard = false;
        int index = 0;

        while (pos < filename.Length && !TestFieldSeps(filename[pos]) && index < fieldSize) {
            char c = filename[pos];
            
            if (c == '*') {
                hasWildcard = true;
                pos++;
                break;
            }
            
            if (c == '?') {
                hasWildcard = true;
            }
            
            index++;
            pos++;
        }

        return (pos, hasWildcard);
    }

    /// <summary>
    /// Extract field from string and pad/convert to proper form.
    /// Handles asterisk conversion to question marks.
    /// </summary>
    private string ExtractAndPadField(string filename, int startPos, int endPos, int fieldSize, bool hasWildcard) {
        StringBuilder result = new();
        int pos = startPos;
        int index = 0;
        bool hitAsterisk = false;

        // Extract actual characters
        while (pos < endPos && !TestFieldSeps(filename[pos]) && index < fieldSize) {
            if (filename[pos] == '*') {
                hitAsterisk = true;
                break;
            }
            
            result.Append(char.ToUpper(filename[pos]));
            index++;
            pos++;
        }

        // If we hit asterisk, fill rest with ?
        if (hitAsterisk) {
            while (index < fieldSize) {
                result.Append('?');
                index++;
            }
        } else {
            // Pad with spaces
            while (index < fieldSize) {
                result.Append(' ');
                index++;
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// Gets an FCB wrapper at the given linear address.
    /// </summary>
    /// <param name="fcbAddress">Linear address of the FCB (standard or extended).</param>
    /// <param name="attribute">Not used; kept for compatibility with FreeDOS signature.</param>
    /// <returns>Wrapped <see cref="DosFileControlBlock"/>.</returns>
    public DosFileControlBlock GetFcb(uint fcbAddress, out byte attribute) {
        attribute = 0;
        return new DosFileControlBlock(_memory, fcbAddress);
    }

    /// <summary>
    /// Opens a file using the FCB filename and stores the SFT handle in the FCB.
    /// </summary>
    /// <param name="fcbAddress">Linear address of the FCB.</param>
    /// <returns><see cref="FcbStatus"/> indicating success or failure.</returns>
    public FcbStatus OpenFile(uint fcbAddress) {
        uint baseAddr = GetActualFcbBaseAddress(fcbAddress, out _);
        DosFileControlBlock fcb = new DosFileControlBlock(_memory, baseAddr);
        string fileSpec = fcb.FullFileName;
        if (string.IsNullOrWhiteSpace(fileSpec)) {
            LogFcbWarning("OPEN", baseAddr, "Blank filename");
            return FcbStatus.Error;
        }

        DosFileOperationResult result = _dosFileManager.OpenFileOrDevice(fileSpec, FileAccessMode.ReadWrite);
        if (result.IsError || result.Value == null) {
            LogFcbWarning("OPEN", baseAddr, "OpenFileOrDevice failed");
            return FcbStatus.Error;
        }

        ushort handle = (ushort)result.Value.Value;
        fcb.SftNumber = (byte)handle;
        if (fcb.RecordSize == 0) {
            fcb.RecordSize = DosFileControlBlock.DefaultRecordSize;
        }
        TrackFcbHandle(handle);
        LogFcbDebug("OPEN", baseAddr, fileSpec, FcbStatus.Success);
        return FcbStatus.Success;
    }

    /// <summary>
    /// Closes an FCB-opened file using its stored SFT handle.
    /// </summary>
    /// <param name="fcbAddress">Linear address of the FCB.</param>
    /// <returns><see cref="FcbStatus"/> indicating success or failure.</returns>
    public FcbStatus CloseFile(uint fcbAddress) {
        uint baseAddr = GetActualFcbBaseAddress(fcbAddress, out _);
        DosFileControlBlock fcb = new DosFileControlBlock(_memory, baseAddr);
        ushort handle = fcb.SftNumber;
        if (handle == 0) {
            LogFcbWarning("CLOSE", baseAddr, "Handle is zero");
            return FcbStatus.Error;
        }
        DosFileOperationResult result = _dosFileManager.CloseFileOrDevice(handle);
        if (result.IsError) {
            LogFcbWarning("CLOSE", baseAddr, "CloseFileOrDevice failed");
            return FcbStatus.Error;
        }

        _trackedFcbHandles.Remove(handle);
        LogFcbDebug("CLOSE", baseAddr, fcb.FullFileName, FcbStatus.Success);
        return FcbStatus.Success;
    }

    /// <summary>
    /// Creates a file using the FCB filename and stores the SFT handle in the FCB.
    /// </summary>
    /// <param name="fcbAddress">Linear address of the FCB.</param>
    /// <returns><see cref="FcbStatus"/> indicating success or failure.</returns>
    public FcbStatus CreateFile(uint fcbAddress) {
        uint baseAddr = GetActualFcbBaseAddress(fcbAddress, out _);
        DosFileControlBlock fcb = new DosFileControlBlock(_memory, baseAddr);
        string fileSpec = fcb.FullFileName;
        if (string.IsNullOrWhiteSpace(fileSpec)) {
            LogFcbWarning("CREATE", baseAddr, "Blank filename");
            return FcbStatus.Error;
        }

        DosFileOperationResult result = _dosFileManager.CreateFileUsingHandle(fileSpec, 0);
        if (result.IsError || result.Value == null) {
            LogFcbWarning("CREATE", baseAddr, "CreateFileUsingHandle failed");
            return FcbStatus.Error;
        }

        ushort handle = (ushort)result.Value.Value;
        fcb.SftNumber = (byte)handle;
        if (fcb.RecordSize == 0) {
            fcb.RecordSize = DosFileControlBlock.DefaultRecordSize;
        }
        TrackFcbHandle(handle);
        LogFcbDebug("CREATE", baseAddr, fileSpec, FcbStatus.Success);
        return FcbStatus.Success;
    }

    /// <summary>
    /// INT 21h AH=14h sequential read using the current block/record pointer.
    /// </summary>
    /// <param name="fcbAddress">Linear address of the FCB.</param>
    /// <param name="dtaAddress">Linear address of the DTA buffer.</param>
    /// <returns><see cref="FcbStatus"/> describing the outcome.</returns>
    public FcbStatus SequentialRead(uint fcbAddress, uint dtaAddress) {
        uint baseAddr = GetActualFcbBaseAddress(fcbAddress, out _);
        DosFileControlBlock fcb = new DosFileControlBlock(_memory, baseAddr);
        ushort handle = fcb.SftNumber;
        if (handle == 0) {
            LogFcbWarning("SEQ READ", baseAddr, "Handle is zero");
            return FcbStatus.Error;
        }

        if (fcb.RecordSize == 0) {
            fcb.RecordSize = DosFileControlBlock.DefaultRecordSize;
        }
        int recordSize = fcb.RecordSize;
        uint absoluteRecord = fcb.AbsoluteRecord;
        if (!TryComputeOffset(absoluteRecord, recordSize, out int offset, out FcbStatus offsetStatus)) {
            LogFcbWarning("SEQ READ", baseAddr, "Offset overflow");
            return offsetStatus;
        }
        DosFileOperationResult seek = _dosFileManager.MoveFilePointerUsingHandle(SeekOrigin.Begin, handle, offset);
        if (seek.IsError) {
            LogFcbWarning("SEQ READ", baseAddr, "Seek failed");
            return FcbStatus.Error;
        }

        DosFileOperationResult read = _dosFileManager.ReadFileOrDevice(handle, (ushort)recordSize, dtaAddress);
        if (read.IsError) {
            LogFcbWarning("SEQ READ", baseAddr, "Read failed");
            return FcbStatus.Error;
        }

        ushort len = (ushort)(read.Value ?? 0);
        if (len == 0) {
            return FcbStatus.NoData;
        }
        fcb.NextRecord();
        if (len < recordSize) {
            return FcbStatus.EndOfFile;
        }
        LogFcbDebug("SEQ READ", baseAddr, fcb.FullFileName, FcbStatus.Success);
        return FcbStatus.Success;
    }

    /// <summary>
    /// INT 21h AH=15h sequential write using the current block/record pointer.
    /// </summary>
    /// <param name="fcbAddress">Linear address of the FCB.</param>
    /// <param name="dtaAddress">Linear address of the DTA buffer.</param>
    /// <returns><see cref="FcbStatus"/> describing the outcome.</returns>
    public FcbStatus SequentialWrite(uint fcbAddress, uint dtaAddress) {
        uint baseAddr = GetActualFcbBaseAddress(fcbAddress, out _);
        DosFileControlBlock fcb = new DosFileControlBlock(_memory, baseAddr);
        ushort handle = fcb.SftNumber;
        if (handle == 0) {
            LogFcbWarning("SEQ WRITE", baseAddr, "Handle is zero");
            return FcbStatus.Error;
        }

        if (fcb.RecordSize == 0) {
            fcb.RecordSize = DosFileControlBlock.DefaultRecordSize;
        }
        int recordSize = fcb.RecordSize;
        uint absoluteRecord = fcb.AbsoluteRecord;
        if (!TryComputeOffset(absoluteRecord, recordSize, out int offset, out FcbStatus offsetStatus)) {
            LogFcbWarning("SEQ WRITE", baseAddr, "Offset overflow");
            return offsetStatus;
        }
        DosFileOperationResult seek = _dosFileManager.MoveFilePointerUsingHandle(SeekOrigin.Begin, handle, offset);
        if (seek.IsError) {
            LogFcbWarning("SEQ WRITE", baseAddr, "Seek failed");
            return FcbStatus.Error;
        }

        DosFileOperationResult write = _dosFileManager.WriteToFileOrDevice(handle, (ushort)recordSize, dtaAddress);
        if (write.IsError) {
            LogFcbWarning("SEQ WRITE", baseAddr, "Write failed");
            return FcbStatus.Error;
        }
        ushort len = (ushort)(write.Value ?? 0);
        fcb.NextRecord();
        if (len < recordSize) {
            return FcbStatus.NoData;
        }
        LogFcbDebug("SEQ WRITE", baseAddr, fcb.FullFileName, FcbStatus.Success);
        return FcbStatus.Success;
    }

    /// <summary>
    /// INT 21h AH=21h random read using the RandomRecord field.
    /// </summary>
    /// <param name="fcbAddress">Linear address of the FCB.</param>
    /// <param name="dtaAddress">Linear address of the DTA buffer.</param>
    /// <returns><see cref="FcbStatus"/> describing the outcome.</returns>
    public FcbStatus RandomRead(uint fcbAddress, uint dtaAddress) {
        uint baseAddr = GetActualFcbBaseAddress(fcbAddress, out _);
        DosFileControlBlock fcb = new DosFileControlBlock(_memory, baseAddr);
        ushort handle = fcb.SftNumber;
        if (handle == 0) {
            LogFcbWarning("RAND READ", baseAddr, "Handle is zero");
            return FcbStatus.Error;
        }

        if (fcb.RecordSize == 0) {
            fcb.RecordSize = DosFileControlBlock.DefaultRecordSize;
        }
        int recordSize = fcb.RecordSize;
        fcb.CalculateRecordPosition();
        uint absoluteRecord = fcb.AbsoluteRecord;
        if (!TryComputeOffset(absoluteRecord, recordSize, out int offset, out FcbStatus offsetStatus)) {
            LogFcbWarning("RAND READ", baseAddr, "Offset overflow");
            return offsetStatus;
        }
        DosFileOperationResult seek = _dosFileManager.MoveFilePointerUsingHandle(SeekOrigin.Begin, handle, offset);
        if (seek.IsError) {
            LogFcbWarning("RAND READ", baseAddr, "Seek failed");
            return FcbStatus.Error;
        }

        DosFileOperationResult read = _dosFileManager.ReadFileOrDevice(handle, (ushort)recordSize, dtaAddress);
        if (read.IsError) {
            LogFcbWarning("RAND READ", baseAddr, "Read failed");
            return FcbStatus.Error;
        }
        ushort len = (ushort)(read.Value ?? 0);
        if (len == 0) {
            return FcbStatus.NoData;
        }
        if (len < recordSize) {
            return FcbStatus.EndOfFile;
        }
        LogFcbDebug("RAND READ", baseAddr, fcb.FullFileName, FcbStatus.Success);
        return FcbStatus.Success;
    }

    /// <summary>
    /// INT 21h AH=22h random write using the RandomRecord field.
    /// </summary>
    /// <param name="fcbAddress">Linear address of the FCB.</param>
    /// <param name="dtaAddress">Linear address of the DTA buffer.</param>
    /// <returns><see cref="FcbStatus"/> describing the outcome.</returns>
    public FcbStatus RandomWrite(uint fcbAddress, uint dtaAddress) {
        uint baseAddr = GetActualFcbBaseAddress(fcbAddress, out _);
        DosFileControlBlock fcb = new DosFileControlBlock(_memory, baseAddr);
        ushort handle = fcb.SftNumber;
        if (handle == 0) {
            LogFcbWarning("RAND WRITE", baseAddr, "Handle is zero");
            return FcbStatus.Error;
        }

        if (fcb.RecordSize == 0) {
            fcb.RecordSize = DosFileControlBlock.DefaultRecordSize;
        }
        int recordSize = fcb.RecordSize;
        fcb.CalculateRecordPosition();
        uint absoluteRecord = fcb.AbsoluteRecord;
        if (!TryComputeOffset(absoluteRecord, recordSize, out int offset, out FcbStatus offsetStatus)) {
            LogFcbWarning("RAND WRITE", baseAddr, "Offset overflow");
            return offsetStatus;
        }
        DosFileOperationResult seek = _dosFileManager.MoveFilePointerUsingHandle(SeekOrigin.Begin, handle, offset);
        if (seek.IsError) {
            LogFcbWarning("RAND WRITE", baseAddr, "Seek failed");
            return FcbStatus.Error;
        }

        DosFileOperationResult write = _dosFileManager.WriteToFileOrDevice(handle, (ushort)recordSize, dtaAddress);
        if (write.IsError) {
            LogFcbWarning("RAND WRITE", baseAddr, "Write failed");
            return FcbStatus.Error;
        }
        ushort len = (ushort)(write.Value ?? 0);
        if (len < recordSize) {
            return FcbStatus.NoData;
        }
        LogFcbDebug("RAND WRITE", baseAddr, fcb.FullFileName, FcbStatus.Success);
        return FcbStatus.Success;
    }

    /// <summary>
    /// INT 21h AH=27h random block read from the RandomRecord pointer.
    /// </summary>
    /// <param name="fcbAddress">Linear address of the FCB.</param>
    /// <param name="dtaAddress">Linear address of the DTA buffer.</param>
    /// <param name="recordCount">On input, requested record count; on output, number actually read.</param>
    /// <returns><see cref="FcbStatus"/> describing the outcome.</returns>
    public FcbStatus RandomBlockRead(uint fcbAddress, uint dtaAddress, ref ushort recordCount) {
        uint baseAddr = GetActualFcbBaseAddress(fcbAddress, out _);
        DosFileControlBlock fcb = new DosFileControlBlock(_memory, baseAddr);
        ushort handle = fcb.SftNumber;
        if (handle == 0) {
            LogFcbWarning("RAND BLK READ", baseAddr, "Handle is zero");
            return FcbStatus.Error;
        }
        if (fcb.RecordSize == 0) {
            fcb.RecordSize = DosFileControlBlock.DefaultRecordSize;
        }
        int recordSize = fcb.RecordSize;
        uint startRecord = fcb.RandomRecord;
        ushort totalRead = 0;
        for (ushort i = 0; i < recordCount; i++) {
            uint absoluteRecord = startRecord + i;
            if (!TryComputeOffset(absoluteRecord, recordSize, out int offset, out FcbStatus offsetStatus)) {
                recordCount = totalRead;
                return offsetStatus;
            }
            DosFileOperationResult seek = _dosFileManager.MoveFilePointerUsingHandle(SeekOrigin.Begin, handle, offset);
            if (seek.IsError) {
                break;
            }
            DosFileOperationResult read = _dosFileManager.ReadFileOrDevice(handle, (ushort)recordSize, dtaAddress);
            if (read.IsError) {
                break;
            }
            ushort len = (ushort)(read.Value ?? 0);
            if (len == 0) {
                break;
            }
            totalRead++;
            if (len < recordSize) {
                break;
            }
        }
        recordCount = totalRead;
        return totalRead == 0 ? FcbStatus.NoData : FcbStatus.Success;
    }

    /// <summary>
    /// INT 21h AH=28h random block write from the RandomRecord pointer.
    /// </summary>
    /// <param name="fcbAddress">Linear address of the FCB.</param>
    /// <param name="dtaAddress">Linear address of the DTA buffer.</param>
    /// <param name="recordCount">On input, requested record count; on output, number actually written.</param>
    /// <returns><see cref="FcbStatus"/> describing the outcome.</returns>
    public FcbStatus RandomBlockWrite(uint fcbAddress, uint dtaAddress, ref ushort recordCount) {
        uint baseAddr = GetActualFcbBaseAddress(fcbAddress, out _);
        DosFileControlBlock fcb = new DosFileControlBlock(_memory, baseAddr);
        ushort handle = fcb.SftNumber;
        if (handle == 0) {
            LogFcbWarning("RAND BLK WRITE", baseAddr, "Handle is zero");
            return FcbStatus.Error;
        }
        if (fcb.RecordSize == 0) {
            fcb.RecordSize = DosFileControlBlock.DefaultRecordSize;
        }
        int recordSize = fcb.RecordSize;
        uint startRecord = fcb.RandomRecord;
        if (recordCount == 0) {
            // Truncate file to random record position
            if (!TryComputeOffset(startRecord, recordSize, out int offset, out FcbStatus offsetStatus)) {
                return offsetStatus;
            }
            DosFileOperationResult seek = _dosFileManager.MoveFilePointerUsingHandle(SeekOrigin.Begin, handle, offset);
            if (seek.IsError) {
                LogFcbWarning("RAND BLK WRITE", baseAddr, "Seek failed during truncate");
                return FcbStatus.Error;
            }
            LogFcbDebug("RAND BLK WRITE", baseAddr, fcb.FullFileName, FcbStatus.Success);
            return FcbStatus.Success;
        }

        ushort requestedRecords = recordCount;
        ushort totalWritten = 0;
        for (ushort i = 0; i < recordCount; i++) {
            uint absoluteRecord = startRecord + i;
            if (!TryComputeOffset(absoluteRecord, recordSize, out int offset, out FcbStatus offsetStatus)) {
                recordCount = totalWritten;
                return offsetStatus;
            }
            DosFileOperationResult seek = _dosFileManager.MoveFilePointerUsingHandle(SeekOrigin.Begin, handle, offset);
            if (seek.IsError) {
                break;
            }
            DosFileOperationResult write = _dosFileManager.WriteToFileOrDevice(handle, (ushort)recordSize, dtaAddress);
            if (write.IsError) {
                break;
            }
            ushort len = (ushort)(write.Value ?? 0);
            if (len < recordSize) {
                totalWritten++;
                break;
            }
            totalWritten++;
        }
        recordCount = totalWritten;
        if (totalWritten == 0) {
            return FcbStatus.NoData;
        }
        if (totalWritten < requestedRecords) {
            return FcbStatus.NoData;
        }
        LogFcbDebug("RAND BLK WRITE", baseAddr, fcb.FullFileName, FcbStatus.Success);
        return FcbStatus.Success;
    }

    /// <summary>
    /// INT 21h AH=23h populates RandomRecord with file size in records.
    /// </summary>
    /// <param name="fcbAddress">Linear address of the FCB.</param>
    /// <returns><see cref="FcbStatus"/> describing the outcome.</returns>
    public FcbStatus GetFileSize(uint fcbAddress) {
        uint baseAddr = GetActualFcbBaseAddress(fcbAddress, out _);
        DosFileControlBlock fcb = new DosFileControlBlock(_memory, baseAddr);
        string fileSpec = fcb.FullFileName;
        if (string.IsNullOrWhiteSpace(fileSpec)) {
            LogFcbWarning("GET SIZE", baseAddr, "Blank filename");
            return FcbStatus.Error;
        }
        DosFileOperationResult open = _dosFileManager.OpenFileOrDevice(fileSpec, FileAccessMode.ReadOnly);
        if (open.IsError || open.Value == null) {
            LogFcbWarning("GET SIZE", baseAddr, "OpenFileOrDevice failed");
            return FcbStatus.Error;
        }
        ushort handle = (ushort)open.Value.Value;
        VirtualFileBase? vf = _dosFileManager.OpenFiles[handle];
        if (vf is DosFile dosFile) {
            long size = dosFile.Length;
            int recSize = fcb.RecordSize == 0 ? DosFileControlBlock.DefaultRecordSize : fcb.RecordSize;
            uint records = (uint)(size / recSize);
            fcb.RandomRecord = records;
            _dosFileManager.CloseFileOrDevice(handle);
            LogFcbDebug("GET SIZE", baseAddr, fileSpec, FcbStatus.Success);
            return FcbStatus.Success;
        }
        _dosFileManager.CloseFileOrDevice(handle);
        LogFcbWarning("GET SIZE", baseAddr, "Not a DOS file");
        return FcbStatus.Error;
    }

    /// <summary>
    /// INT 21h AH=13h delete files matching the FCB pattern.
    /// </summary>
    /// <param name="fcbAddress">Linear address of the FCB.</param>
    /// <returns><see cref="FcbStatus"/> describing the outcome.</returns>
    public FcbStatus DeleteFile(uint fcbAddress) {
        uint baseAddr = GetActualFcbBaseAddress(fcbAddress, out _);
        DosFileControlBlock fcb = new DosFileControlBlock(_memory, baseAddr);
        string pattern = fcb.FullFileName;
        if (string.IsNullOrWhiteSpace(pattern)) {
            LogFcbWarning("DELETE", baseAddr, "Blank pattern");
            return FcbStatus.Error;
        }
        DosFileOperationResult ff = _dosFileManager.FindFirstMatchingFile(pattern, 0);
        if (ff.IsError) {
            return FcbStatus.Success; // per RBIL: still return success if no matches
        }
        while (true) {
            DosDiskTransferArea dta = new DosDiskTransferArea(_memory, MemoryUtils.ToPhysicalAddress(
                _dosFileManager.DiskTransferAreaAddressSegment, _dosFileManager.DiskTransferAreaAddressOffset));
            string dosName = dta.FileName;
            DosFileOperationResult del = _dosFileManager.RemoveFile(dosName);
            if (del.IsError) {
                LogFcbWarning("DELETE", baseAddr, "RemoveFile failed");
                return FcbStatus.Error;
            }
            DosFileOperationResult nx = _dosFileManager.FindNextMatchingFile();
            if (nx.IsError) {
                break;
            }
        }
        LogFcbDebug("DELETE", baseAddr, pattern, FcbStatus.Success);
        return FcbStatus.Success;
    }

    /// <summary>
    /// INT 21h AH=17h rename files using FCB old/new patterns (supports wildcards).
    /// </summary>
    /// <param name="fcbAddress">Linear address of the FCB.</param>
    /// <returns><see cref="FcbStatus"/> describing the outcome.</returns>
    public FcbStatus RenameFile(uint fcbAddress) {
        // Special FCB layout for rename: old name/ext then new name/ext in reserved region
        uint baseAddr = GetActualFcbBaseAddress(fcbAddress, out _);
        DosFileControlBlock fcb = new DosFileControlBlock(_memory, baseAddr);

        string oldName = GetRenameOldName(fcb);
        string newPattern = GetRenameNewName(baseAddr);
        if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newPattern)) {
            LogFcbWarning("RENAME", baseAddr, "Missing old/new names");
            return FcbStatus.Error;
        }

        DosFileOperationResult ff = _dosFileManager.FindFirstMatchingFile(oldName, 0);
        if (ff.IsError) {
            return FcbStatus.Error;
        }
        while (true) {
            DosDiskTransferArea dta = new DosDiskTransferArea(_memory, MemoryUtils.ToPhysicalAddress(
                _dosFileManager.DiskTransferAreaAddressSegment, _dosFileManager.DiskTransferAreaAddressOffset));
            string sourceDos = dta.FileName;
            string destDos = ApplyWildcardRename(sourceDos, newPattern);
            try {
                string? srcHost = _dosFileManager.TryGetFullHostPathFromDos(sourceDos);
                string? dstHost = _dosFileManager.TryGetFullHostPathFromDos(destDos);
                if (!string.IsNullOrWhiteSpace(srcHost) && !string.IsNullOrWhiteSpace(dstHost)) {
                    File.Move(srcHost, dstHost);
                } else {
                    LogFcbWarning("RENAME", baseAddr, "Unable to resolve host paths");
                    return FcbStatus.Error;
                }
            } catch (IOException) {
                LogFcbWarning("RENAME", baseAddr, "IOException during rename");
                return FcbStatus.Error;
            }

            DosFileOperationResult nx = _dosFileManager.FindNextMatchingFile();
            if (nx.IsError) {
                break;
            }
        }
        LogFcbDebug("RENAME", baseAddr, oldName + "->" + newPattern, FcbStatus.Success);
        return FcbStatus.Success;
    }

    public void SetRandomRecordNumber(uint fcbAddress) {
        uint fcbBase = GetActualFcbBaseAddress(fcbAddress, out _);
        DosFileControlBlock fcb = new DosFileControlBlock(_memory, fcbBase);
        uint absoluteRecord = (uint)(fcb.CurrentBlock * 128 + fcb.CurrentRecord);
        fcb.RandomRecord = absoluteRecord;
    }

    /// <summary>
    /// INT 21h AH=11h FCB Find First using the FCB filename pattern.
    /// </summary>
    /// <param name="fcbAddress">Linear address of the FCB.</param>
    /// <param name="dtaAddress">Linear address of the DTA buffer.</param>
    /// <returns><see cref="FcbStatus"/> describing the outcome.</returns>
    public FcbStatus FindFirst(uint fcbAddress, uint dtaAddress) {
        uint baseAddr = GetActualFcbBaseAddress(fcbAddress, out _);
        DosFileControlBlock fcb = new DosFileControlBlock(_memory, baseAddr);
        string pattern = fcb.FullFileName;
        DosFileOperationResult result = _dosFileManager.FindFirstMatchingFile(pattern, 0);
        FcbStatus status = result.IsError ? FcbStatus.Error : FcbStatus.Success;
        LogFcbDebug("FIND FIRST", baseAddr, pattern, status);
        return status;
    }

    /// <summary>
    /// INT 21h AH=12h FCB Find Next using prior search state.
    /// </summary>
    /// <param name="fcbAddress">Linear address of the FCB (unused, present for symmetry).</param>
    /// <param name="dtaAddress">Linear address of the DTA buffer.</param>
    /// <returns><see cref="FcbStatus"/> describing the outcome.</returns>
    public FcbStatus FindNext(uint fcbAddress, uint dtaAddress) {
        DosFileOperationResult result = _dosFileManager.FindNextMatchingFile();
        FcbStatus status = result.IsError ? FcbStatus.Error : FcbStatus.Success;
        LogFcbDebug("FIND NEXT", fcbAddress, string.Empty, status);
        return status;
    }

    /// <summary>
    /// Clears all DTA search state (FindFirst/FindNext) entries.
    /// </summary>
    public void ClearAllSearchState() {
        _dosFileManager.ClearAllFileSearches();
    }

    /// <summary>
    /// Closes every file handle opened through FCB APIs and clears the tracking list.
    /// </summary>
    public void CloseAllTrackedFcbFiles() {
        foreach (ushort handle in _trackedFcbHandles) {
            DosFileOperationResult result = _dosFileManager.CloseFileOrDevice(handle);
            if (result.IsError && _loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("Failed to close FCB handle {Handle}", handle);
            }
        }
        _trackedFcbHandles.Clear();
    }

    private uint GetActualFcbBaseAddress(uint fcbAddress, out byte attribute) {
        attribute = 0;
        // Extended FCB detection via drive marker 0xFF
        DosFileControlBlock probe = new DosFileControlBlock(_memory, fcbAddress);
        if (probe.DriveNumber == 0xFF) {
            return fcbAddress + 7;
        }
        return fcbAddress;
    }

    private static string GetRenameOldName(DosFileControlBlock fcb) {
        // Offsets: 0x01..0x08 name, 0x09..0x0B ext
        string name = fcb.FullFileName;
        return name;
    }

    private string GetRenameNewName(uint fcbBaseAddress) {
        string newNameField = ReadSpacePaddedField(fcbBaseAddress, RenameNewNameOffset, DosFileControlBlock.FileNameSize);
        string newExtField = ReadSpacePaddedField(fcbBaseAddress, RenameNewExtensionOffset, DosFileControlBlock.FileExtensionSize);
        string trimmedName = newNameField.TrimEnd();
        string trimmedExt = newExtField.TrimEnd();
        if (string.IsNullOrWhiteSpace(trimmedName)) {
            return string.Empty;
        }
        return string.IsNullOrWhiteSpace(trimmedExt) ? trimmedName : trimmedName + "." + trimmedExt;
    }

    private static string ApplyWildcardRename(string oldName, string newPattern) {
        // Apply '?' substitution: copy from oldName when '?' in pattern
        int dotOld = oldName.IndexOf('.');
        string oldBase = dotOld >= 0 ? oldName[..dotOld] : oldName;
        string oldExt = dotOld >= 0 ? oldName[(dotOld + 1)..] : string.Empty;

        int dotNew = newPattern.IndexOf('.');
        string newBasePat = dotNew >= 0 ? newPattern[..dotNew] : newPattern;
        string newExtPat = dotNew >= 0 ? newPattern[(dotNew + 1)..] : string.Empty;

        string newBase = SubstitutePattern(newBasePat, oldBase, DosFileControlBlock.FileNameSize);
        string newExt = SubstitutePattern(newExtPat, oldExt, DosFileControlBlock.FileExtensionSize);
        return string.IsNullOrEmpty(newExt) ? newBase : newBase + "." + newExt;
    }

    private static string SubstitutePattern(string pattern, string source, int maxLen) {
        StringBuilder sb = new StringBuilder();
        int len = Math.Min(maxLen, pattern.Length);
        for (int i = 0; i < len; i++) {
            char pc = pattern[i];
            if (pc == '?') {
                sb.Append(i < source.Length ? source[i] : ' ');
            } else {
                sb.Append(pc);
            }
        }
        if (pattern.Length > len) {
            sb.Append(pattern[len..]);
        }
        return sb.ToString().TrimEnd();
    }

    private bool TryComputeOffset(uint absoluteRecord, int recordSize, out int offset, out FcbStatus statusCode) {
        ulong offsetValue = (ulong)absoluteRecord * (ulong)recordSize;
        if (offsetValue > int.MaxValue) {
            offset = 0;
            statusCode = FcbStatus.SegmentWrap;
            return false;
        }
        offset = (int)offsetValue;
        statusCode = FcbStatus.Success;
        return true;
    }

    private string ReadSpacePaddedField(uint fcbBaseAddress, int offset, int length) {
        StringBuilder builder = new StringBuilder();
        for (int i = 0; i < length; i++) {
            builder.Append((char)_memory.UInt8[(uint)offset + fcbBaseAddress + (uint)i]);
        }
        return builder.ToString();
    }

    private void TrackFcbHandle(ushort handle) {
        _trackedFcbHandles.Add(handle);
    }

    private void LogFcbWarning(string operation, uint fcbBaseAddress, string reason) {
        if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
            _loggerService.Warning("FCB {Operation} failed at 0x{Address:X} because {Reason}", operation, fcbBaseAddress, reason);
        }
    }

    private void LogFcbDebug(string operation, uint fcbBaseAddress, string detail, FcbStatus status) {
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("FCB {Operation} at 0x{Address:X} ({Detail}) -> {Status}", operation, fcbBaseAddress, detail, status);
        }
    }
}

