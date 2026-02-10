namespace Spice86.Core.Emulator.OperatingSystem;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Serilog.Events;

using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

/// <summary>
/// Manages File Control Block (FCB) operations for DOS INT 21h FCB-based file functions.
/// </summary>
/// <remarks>
/// <para>
/// <b>Overview:</b> This class implements legacy CP/M-style File Control Block operations required by many
/// 1980s-1990s DOS programs and games (Civilization 1, Reunion, Detroit, Lands of Lore, etc.). FCBs were
/// the primary file I/O mechanism in early DOS but were superseded by file handles in DOS 2.0+.
/// </para>
/// <para>
/// <b>Implementation Strategy:</b> Follows <b>DOSBox Staging</b> (dos_files.cpp) for FCB behavior,
/// as it has extensive game compatibility testing. FreeDOS kernel (fcbfns.c) is referenced in comments
/// where implementations align. Key differences: DOSBox loops per-record for block I/O, uses floor division
/// for GetFileSize; FreeDOS does bulk I/O, uses ceiling division.
/// </para>
/// <para>
/// <b>Supported Operations:</b> Open/Close (0Fh/10h), Create/Delete/Rename (16h/13h/17h),
/// Sequential R/W (14h/15h), Random R/W (21h/22h), Block R/W (27h/28h), Find First/Next (11h/12h),
/// Get Size/Set Random (23h/24h), Parse Filename (29h). Extended FCB support for file attributes.
/// </para>
/// <para>
/// See <see cref="DosFileControlBlock"/> and <see cref="DosExtendedFileControlBlock"/> for structure details.
/// </para>
/// </remarks>
public class DosFcbManager {

    private const int RenameNewNameOffset = 0x0C;
    private const int RenameNewExtensionOffset = 0x14;

    /// <summary>
    /// Common separator characters for filename parsing.
    /// These characters can appear between components of a DOS path and may be skipped during parsing.
    /// </summary>
    /// <remarks>
    /// DOS filename parser: colon (:), semicolon (;), comma (,), equals (=), plus (+), space, tab
    /// FreeDOS reference: fcbfns.c line 50 "TestCmnSeps"
    /// </remarks>
    public const string CommonSeparators = ":;,=+ \t";

    /// <summary>
    /// Field separator characters that cannot appear in filename or extension.
    /// These characters terminate filename parsing and are never part of a valid DOS filename.
    /// </summary>
    /// <remarks>
    /// Slash (/), backslash (\), quote ("), brackets ([]), angle brackets (&lt;&gt;), pipe (|),
    /// dot (.), colon (:), semicolon (;), comma (,), equals (=), plus (+), tab
    /// FreeDOS reference: fcbfns.c line 51 "TestFieldSeps"
    /// </remarks>
    public const string FieldSeparators = "/\\\"[]<>|.:;,=+\t";

    private readonly IMemory _memory;
    private readonly DosFileManager _dosFileManager;
    private readonly DosDriveManager _dosDriveManager;
    private readonly ILoggerService _loggerService;
    private readonly HashSet<ushort> _trackedFcbHandles = new();

    public
    DosFcbManager(IMemory memory, DosFileManager dosFileManager, DosDriveManager dosDriveManager, ILoggerService loggerService) {
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
    /// <param name="parseControl">Parsing control flags.</param>
    /// <param name="bytesAdvanced">Number of bytes consumed from the input string.</param>
    /// <returns>An <see cref="FcbParseResult"/> describing parse status.</returns>
    public FcbParseResult ParseFilename(uint stringAddress, uint fcbAddress, FcbParseControl parseControl, out uint bytesAdvanced) {
        string filename = _memory.GetZeroTerminatedString(stringAddress, 128);
        DosFileControlBlock fcb = GetFcb(fcbAddress, out _);

        int pos = 0;
        bool retCodeDrive = false;
        bool retCodeExt = false;

        // Skip leading separators if requested
        // FreeDOS: "if (*wTestMode & PARSE_SKIP_LEAD_SEP)"
        if (parseControl.HasFlag(FcbParseControl.SkipLeadingSeparators)) {
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
        } else if (!parseControl.HasFlag(FcbParseControl.SetDefaultDrive)) {
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
        if (!parseControl.HasFlag(FcbParseControl.BlankFilename)) {
            fcb.FileName = "        ";
        }

        // Blank extension field if requested
        if ((parseControl & FcbParseControl.BlankExtension) == 0) {
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
        (pos, bool hasWildcardName) = GetNameField(filename, pos, 8);
        fcb.FileName = ExtractAndPadField(filename, nameStart, pos, 8, hasWildcardName);

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

        if (hasWildcardName || retCodeExt) {
            return FcbParseResult.WildcardsPresent;
        }

        return FcbParseResult.NoWildcards;
    }

    /// <summary>
    /// Checks if character is a common separator.
    /// FreeDOS: TestCmnSeps - ":;,=+ \t"
    /// </summary>
    private static bool TestCmnSeps(char c) {
        return CommonSeparators.Contains(c);
    }

    /// <summary>
    /// Checks if character is a field separator.
    /// FreeDOS: TestFieldSeps - (unsigned char)*lpFileName &lt;= ' ' || "/\\\"[]&lt;&gt;|.:;,=+\t"
    /// </summary>
    private static bool TestFieldSeps(char c) {
        return c <= ' ' || FieldSeparators.Contains(c);
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

        // FreeDOS: After reading fieldSize characters, continue advancing until we hit a separator
        // This ensures pos points to the separator (like '.') and not a character we skipped
        while (pos < filename.Length && !TestFieldSeps(filename[pos])) {
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
    /// Gets an FCB wrapper at the given linear address, supporting both standard and extended FCBs.
    /// </summary>
    /// <param name="fcbAddress">Linear address of the FCB (standard or extended).</param>
    /// <param name="attribute">For extended FCBs, receives the file attributes byte; for standard FCBs, set to 0.</param>
    /// <returns>
    /// An extended FCB wrapper if the address points to 0xFF (extended FCB flag),
    /// otherwise a standard FCB wrapper.
    /// </returns>
    public DosFileControlBlock GetFcb(uint fcbAddress, out byte attribute) {
        attribute = 0;

        byte flag = _memory.UInt8[fcbAddress];
        if (flag == DosExtendedFileControlBlock.ExtendedFcbFlag) {
            // Extended FCB: header at fcbAddress, standard FCB starts at fcbAddress + 7
            DosExtendedFileControlBlock xfcb = new DosExtendedFileControlBlock(_memory, fcbAddress);
            attribute = xfcb.Attribute;
            return xfcb;
        }

        // Standard FCB
        return new DosFileControlBlock(_memory, fcbAddress);
    }

    /// <summary>
    /// Opens a file using the FCB filename and stores the SFT handle in the FCB.
    /// </summary>
    /// <param name="fcbAddress">Linear address of the FCB.</param>
    /// <returns><see cref="FcbStatus"/> indicating success or failure.</returns>
    public FcbStatus OpenFile(uint fcbAddress) {
        uint baseAddr = GetActualFcbBaseAddress(fcbAddress);
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
        // Reset block/record pointers on open (FreeDOS behavior)
        fcb.CurrentBlock = 0;
        fcb.CurrentRecord = 0;
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
        uint baseAddr = GetActualFcbBaseAddress(fcbAddress);
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
        uint baseAddr = GetActualFcbBaseAddress(fcbAddress);
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
    /// <remarks>
    /// DOSBox Staging reference: dos_files.cpp:1472-1503 DOS_FCBRead.
    /// Key behaviors:
    /// - Auto-reopens closed FCBs if handle=0xFF but rec_size!=0 (line 1478-1481)
    /// - Zero-pads partial reads to full record size (line 1494-1496)
    /// - Advances block/record position after read (line 1499-1500)
    /// </remarks>
    /// <param name="fcbAddress">Linear address of the FCB.</param>
    /// <param name="dtaAddress">Linear address of the DTA buffer.</param>
    /// <returns><see cref="FcbStatus"/> describing the outcome.</returns>
    public FcbStatus SequentialRead(uint fcbAddress, uint dtaAddress) {
        uint baseAddr = GetActualFcbBaseAddress(fcbAddress);
        DosFileControlBlock fcb = new DosFileControlBlock(_memory, baseAddr);
        ushort handle = fcb.SftNumber;

        // DOSBox: Auto-reopen closed FCB if handle=0xFF but rec_size!=0 (dos_files.cpp:1478-1481)
        if (handle == 0xFF && fcb.RecordSize != 0) {
            FcbStatus reopenStatus = OpenFile(fcbAddress);
            if (reopenStatus != FcbStatus.Success) {
                LogFcbWarning("SEQ READ", baseAddr, "Auto-reopen failed");
                return FcbStatus.NoData;
            }
            fcb = new DosFileControlBlock(_memory, baseAddr);
            handle = fcb.SftNumber;
            LogFcbDebug("SEQ READ", baseAddr, "Reopened closed FCB", FcbStatus.Success);
        }

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

        // DOSBox: Zero-pad partial reads to full record size (dos_files.cpp:1494-1496)
        if (len < recordSize) {
            ZeroPadDta(dtaAddress, len, recordSize);
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
    /// <remarks>
    /// DOSBox Staging reference: dos_files.cpp:1505-1540 DOS_FCBWrite.
    /// Key behaviors:
    /// - Auto-reopens closed FCBs if handle=0xFF but rec_size!=0 (line 1511-1514)
    /// - Updates FCB size/date/time after successful write (line 1526-1536)
    /// - Advances block/record position after write (line 1537-1538)
    /// </remarks>
    /// <param name="fcbAddress">Linear address of the FCB.</param>
    /// <param name="dtaAddress">Linear address of the DTA buffer.</param>
    /// <returns><see cref="FcbStatus"/> describing the outcome.</returns>
    public FcbStatus SequentialWrite(uint fcbAddress, uint dtaAddress) {
        uint baseAddr = GetActualFcbBaseAddress(fcbAddress);
        DosFileControlBlock fcb = new DosFileControlBlock(_memory, baseAddr);
        ushort handle = fcb.SftNumber;

        // DOSBox: Auto-reopen closed FCB if handle=0xFF but rec_size!=0 (dos_files.cpp:1511-1514)
        if (handle == 0xFF && fcb.RecordSize != 0) {
            FcbStatus reopenStatus = OpenFile(fcbAddress);
            if (reopenStatus != FcbStatus.Success) {
                LogFcbWarning("SEQ WRITE", baseAddr, "Auto-reopen failed");
                return FcbStatus.NoData;
            }
            fcb = new DosFileControlBlock(_memory, baseAddr);
            handle = fcb.SftNumber;
            LogFcbDebug("SEQ WRITE", baseAddr, "Reopened closed FCB", FcbStatus.Success);
        }

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

        // DOSBox: Update FCB size/date/time after successful write (dos_files.cpp:1526-1536)
        UpdateFcbAfterWrite(fcb, handle, offset, len);

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
    /// <remarks>
    /// DOSBox Staging reference: dos_files.cpp:1571-1602 DOS_FCBRandomRead.
    /// Key behaviors:
    /// - Sets block/record from random field before read (line 1587)
    /// - For single-record read (AH=21h), restores block/record after read (line 1588, 1598)
    /// - Zero-pads partial reads via DOS_FCBRead (line 1494-1496)
    /// </remarks>
    /// <param name="fcbAddress">Linear address of the FCB.</param>
    /// <param name="dtaAddress">Linear address of the DTA buffer.</param>
    /// <returns><see cref="FcbStatus"/> describing the outcome.</returns>
    public FcbStatus RandomRead(uint fcbAddress, uint dtaAddress) {
        uint baseAddr = GetActualFcbBaseAddress(fcbAddress);
        DosFileControlBlock fcb = new DosFileControlBlock(_memory, baseAddr);
        ushort handle = fcb.SftNumber;

        // DOSBox: Auto-reopen closed FCB if handle=0xFF but rec_size!=0
        if (handle == 0xFF && fcb.RecordSize != 0) {
            FcbStatus reopenStatus = OpenFile(fcbAddress);
            if (reopenStatus != FcbStatus.Success) {
                LogFcbWarning("RAND READ", baseAddr, "Auto-reopen failed");
                return FcbStatus.NoData;
            }
            fcb = new DosFileControlBlock(_memory, baseAddr);
            handle = fcb.SftNumber;
        }

        if (handle == 0) {
            LogFcbWarning("RAND READ", baseAddr, "Handle is zero");
            return FcbStatus.Error;
        }

        if (fcb.RecordSize == 0) {
            fcb.RecordSize = DosFileControlBlock.DefaultRecordSize;
        }
        int recordSize = fcb.RecordSize;

        // DOSBox: Set block/record from random field (dos_files.cpp:1587)
        fcb.CalculateRecordPosition();

        // DOSBox: Store old block/record for restore after read (dos_files.cpp:1588)
        ushort oldBlock = fcb.CurrentBlock;
        byte oldRecord = fcb.CurrentRecord;

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

        // DOSBox: Zero-pad partial reads to full record size
        if (len > 0 && len < recordSize) {
            ZeroPadDta(dtaAddress, len, recordSize);
        }

        // DOSBox: Restore old block/record for single-record random read (dos_files.cpp:1598)
        fcb.CurrentBlock = oldBlock;
        fcb.CurrentRecord = oldRecord;

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
    /// <remarks>
    /// DOSBox Staging reference: dos_files.cpp:1604-1633 DOS_FCBRandomWrite.
    /// Key behaviors:
    /// - Sets block/record from random field before write (line 1615)
    /// - For single-record write (AH=22h), restores block/record after write (line 1616, 1629)
    /// - Updates FCB size/date/time via DOS_FCBWrite (line 1526-1536)
    /// </remarks>
    /// <param name="fcbAddress">Linear address of the FCB.</param>
    /// <param name="dtaAddress">Linear address of the DTA buffer.</param>
    /// <returns><see cref="FcbStatus"/> describing the outcome.</returns>
    public FcbStatus RandomWrite(uint fcbAddress, uint dtaAddress) {
        uint baseAddr = GetActualFcbBaseAddress(fcbAddress);
        DosFileControlBlock fcb = new DosFileControlBlock(_memory, baseAddr);
        ushort handle = fcb.SftNumber;

        // DOSBox: Auto-reopen closed FCB if handle=0xFF but rec_size!=0
        if (handle == 0xFF && fcb.RecordSize != 0) {
            FcbStatus reopenStatus = OpenFile(fcbAddress);
            if (reopenStatus != FcbStatus.Success) {
                LogFcbWarning("RAND WRITE", baseAddr, "Auto-reopen failed");
                return FcbStatus.NoData;
            }
            fcb = new DosFileControlBlock(_memory, baseAddr);
            handle = fcb.SftNumber;
        }

        if (handle == 0) {
            LogFcbWarning("RAND WRITE", baseAddr, "Handle is zero");
            return FcbStatus.Error;
        }

        if (fcb.RecordSize == 0) {
            fcb.RecordSize = DosFileControlBlock.DefaultRecordSize;
        }
        int recordSize = fcb.RecordSize;

        // DOSBox: Set block/record from random field (dos_files.cpp:1615)
        fcb.CalculateRecordPosition();

        // DOSBox: Store old block/record for restore after write (dos_files.cpp:1616)
        ushort oldBlock = fcb.CurrentBlock;
        byte oldRecord = fcb.CurrentRecord;

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

        // DOSBox: Update FCB size/date/time after successful write (dos_files.cpp:1526-1536)
        UpdateFcbAfterWrite(fcb, handle, offset, len);

        // DOSBox: Restore old block/record for single-record random write (dos_files.cpp:1629)
        fcb.CurrentBlock = oldBlock;
        fcb.CurrentRecord = oldRecord;

        if (len < recordSize) {
            return FcbStatus.NoData;
        }
        LogFcbDebug("RAND WRITE", baseAddr, fcb.FullFileName, FcbStatus.Success);
        return FcbStatus.Success;
    }

    /// <summary>
    /// INT 21h AH=27h random block read from the RandomRecord pointer.
    /// </summary>
    /// <remarks>
    /// Implementation follows DOSBox Staging dos_files.cpp:1571-1602 DOS_FCBRandomRead.
    /// Key behaviors:
    /// - Sets block/record from random field (line 1587)
    /// - Loops per-record calling DOS_FCBRead (line 1590-1593)
    /// - Updates block/record and RandomRecord after read (line 1596-1600)
    /// - Zero-pads partial reads via DOS_FCBRead (line 1494-1496)
    /// FreeDOS (fcbfns.c:230-283) does a single DosRWSft operation instead.
    /// </remarks>
    /// <param name="fcbAddress">Linear address of the FCB.</param>
    /// <param name="dtaAddress">Linear address of the DTA buffer.</param>
    /// <param name="recordCount">On input, requested record count; on output, number actually read.</param>
    /// <returns><see cref="FcbStatus"/> describing the outcome.</returns>
    public FcbStatus RandomBlockRead(uint fcbAddress, uint dtaAddress, ref ushort recordCount) {
        uint baseAddr = GetActualFcbBaseAddress(fcbAddress);
        DosFileControlBlock fcb = new DosFileControlBlock(_memory, baseAddr);
        ushort handle = fcb.SftNumber;

        // DOSBox: Auto-reopen closed FCB if handle=0xFF but rec_size!=0
        if (handle == 0xFF && fcb.RecordSize != 0) {
            FcbStatus reopenStatus = OpenFile(fcbAddress);
            if (reopenStatus != FcbStatus.Success) {
                LogFcbWarning("RAND BLK READ", baseAddr, "Auto-reopen failed");
                recordCount = 0;
                return FcbStatus.NoData;
            }
            fcb = new DosFileControlBlock(_memory, baseAddr);
            handle = fcb.SftNumber;
        }

        if (handle == 0) {
            LogFcbWarning("RAND BLK READ", baseAddr, "Handle is zero");
            return FcbStatus.Error;
        }
        if (fcb.RecordSize == 0) {
            fcb.RecordSize = DosFileControlBlock.DefaultRecordSize;
        }
        int recordSize = fcb.RecordSize;
        uint startRecord = fcb.RandomRecord;

        // DOSBox: Set block/record from random field (dos_files.cpp:1587)
        fcb.CalculateRecordPosition();

        ushort totalRead = 0;
        FcbStatus lastError = FcbStatus.Success;

        // DOSBox: loop for each record (dos_files.cpp:1590-1593)
        for (ushort i = 0; i < recordCount; i++) {
            uint absoluteRecord = startRecord + i;
            if (!TryComputeOffset(absoluteRecord, recordSize, out int offset, out FcbStatus offsetStatus)) {
                lastError = offsetStatus;
                break;
            }
            DosFileOperationResult seek = _dosFileManager.MoveFilePointerUsingHandle(SeekOrigin.Begin, handle, offset);
            if (seek.IsError) {
                break;
            }
            // Advance DTA destination for each record
            uint destinationAddress = dtaAddress + (uint)totalRead * (uint)recordSize;
            DosFileOperationResult read = _dosFileManager.ReadFileOrDevice(handle, (ushort)recordSize, destinationAddress);
            if (read.IsError) {
                break;
            }
            ushort len = (ushort)(read.Value ?? 0);
            if (len == 0) {
                lastError = FcbStatus.NoData;
                break;
            }

            // DOSBox: Zero-pad partial reads to full record size (dos_files.cpp:1494-1496)
            if (len < recordSize) {
                ZeroPadDta(destinationAddress, len, recordSize);
            }

            totalRead++;

            // DOSBox: Advance block/record after each read (dos_files.cpp:1499-1500)
            if (++fcb.CurrentRecord > 127) {
                fcb.CurrentRecord = 0;
                fcb.CurrentBlock++;
            }

            if (len < recordSize) {
                lastError = FcbStatus.EndOfFile;
                break;
            }
        }

        // DOSBox: Update RandomRecord with new position (dos_files.cpp:1600)
        fcb.RandomRecord = (uint)fcb.CurrentBlock * 128 + fcb.CurrentRecord;

        recordCount = totalRead;
        if (totalRead == 0) {
            return FcbStatus.NoData;
        }
        return lastError == FcbStatus.EndOfFile ? FcbStatus.EndOfFile : FcbStatus.Success;
    }

    /// <summary>
    /// INT 21h AH=28h random block write from the RandomRecord pointer.
    /// </summary>
    /// <remarks>
    /// Implementation follows DOSBox Staging dos_files.cpp:1604-1633 DOS_FCBRandomWrite.
    /// Key behaviors:
    /// - Sets block/record from random field (line 1615)
    /// - Special handling for CX=0: truncate/extend file via DOS_FCBIncreaseSize (line 1624-1626)
    /// - Loops per-record calling DOS_FCBWrite (line 1619-1622)
    /// - Updates block/record and RandomRecord after write (line 1627-1631)
    /// - Updates FCB size/date/time via DOS_FCBWrite (line 1526-1536)
    /// </remarks>
    /// <param name="fcbAddress">Linear address of the FCB.</param>
    /// <param name="dtaAddress">Linear address of the DTA buffer.</param>
    /// <param name="recordCount">On input, requested record count; on output, number actually written.</param>
    /// <returns><see cref="FcbStatus"/> describing the outcome.</returns>
    public FcbStatus RandomBlockWrite(uint fcbAddress, uint dtaAddress, ref ushort recordCount) {
        uint baseAddr = GetActualFcbBaseAddress(fcbAddress);
        DosFileControlBlock fcb = new DosFileControlBlock(_memory, baseAddr);
        ushort handle = fcb.SftNumber;

        // DOSBox: Auto-reopen closed FCB if handle=0xFF but rec_size!=0
        if (handle == 0xFF && fcb.RecordSize != 0) {
            FcbStatus reopenStatus = OpenFile(fcbAddress);
            if (reopenStatus != FcbStatus.Success) {
                LogFcbWarning("RAND BLK WRITE", baseAddr, "Auto-reopen failed");
                recordCount = 0;
                return FcbStatus.NoData;
            }
            fcb = new DosFileControlBlock(_memory, baseAddr);
            handle = fcb.SftNumber;
        }

        if (handle == 0) {
            LogFcbWarning("RAND BLK WRITE", baseAddr, "Handle is zero");
            return FcbStatus.Error;
        }
        if (fcb.RecordSize == 0) {
            fcb.RecordSize = DosFileControlBlock.DefaultRecordSize;
        }
        int recordSize = fcb.RecordSize;
        uint startRecord = fcb.RandomRecord;

        // DOSBox: Set block/record from random field (dos_files.cpp:1615)
        fcb.CalculateRecordPosition();

        if (recordCount == 0) {
            // Truncate/extend file to random record position
            // DOSBox: DOS_FCBIncreaseSize (dos_files.cpp:1624-1626, 1542-1569)
            if (!TryComputeOffset(startRecord, recordSize, out int offset, out FcbStatus offsetStatus)) {
                return offsetStatus;
            }
            VirtualFileBase? vf = _dosFileManager.OpenFiles[handle];
            if (vf is DosFile dosFile) {
                dosFile.SetLength(offset);
                // DOSBox: Update size/date/time (dos_files.cpp:1554-1566)
                fcb.FileSize = (uint)offset;
                UpdateFcbDateTime(fcb);
                LogFcbDebug("RAND BLK WRITE", baseAddr, fcb.FullFileName, FcbStatus.Success);
                return FcbStatus.Success;
            }
            LogFcbWarning("RAND BLK WRITE", baseAddr, "Handle is not a file");
            return FcbStatus.Error;
        }

        ushort requestedRecords = recordCount;
        ushort totalWritten = 0;

        // DOSBox: loop for each record (dos_files.cpp:1619-1622)
        for (ushort i = 0; i < recordCount; i++) {
            uint absoluteRecord = startRecord + i;
            if (!TryComputeOffset(absoluteRecord, recordSize, out int offset, out FcbStatus offsetStatus)) {
                break;
            }
            DosFileOperationResult seek = _dosFileManager.MoveFilePointerUsingHandle(SeekOrigin.Begin, handle, offset);
            if (seek.IsError) {
                break;
            }

            // DOSBox: Read from DTA at recno*rec_size offset (dos_files.cpp:1523)
            uint sourceAddress = dtaAddress + (uint)i * (uint)recordSize;
            DosFileOperationResult write = _dosFileManager.WriteToFileOrDevice(handle, (ushort)recordSize, sourceAddress);
            if (write.IsError) {
                break;
            }
            ushort len = (ushort)(write.Value ?? 0);

            // DOSBox: Update FCB size/date/time after successful write (dos_files.cpp:1526-1536)
            UpdateFcbAfterWrite(fcb, handle, offset, len);

            totalWritten++;

            // DOSBox: Advance block/record after each write (dos_files.cpp:1537-1538)
            if (++fcb.CurrentRecord > 127) {
                fcb.CurrentRecord = 0;
                fcb.CurrentBlock++;
            }

            if (len < recordSize) {
                break;
            }
        }

        // DOSBox: Update RandomRecord with new position (dos_files.cpp:1631)
        fcb.RandomRecord = (uint)fcb.CurrentBlock * 128 + fcb.CurrentRecord;

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
    /// INT 21h AH=23h - Get File Size in Records.
    /// Computes the number of records needed to contain the file and stores it in the FCB's RandomRecord field.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Purpose:</b> This function determines how many records (of the size specified in the FCB's RecordSize field)
    /// are needed to store the entire file. This is useful for applications that want to know the file's size
    /// in terms of their record structure before performing random access I/O.
    /// </para>
    /// <para>
    /// <b>How it works:</b>
    /// <list type="number">
    ///   <item>Opens the file specified in the FCB (read-only)</item>
    ///   <item>Gets the file size in bytes</item>
    ///   <item>Divides file size by record size to get number of records</item>
    ///   <item>Stores the result in FCB.RandomRecord</item>
    ///   <item>Closes the file</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Record Size:</b> If FCB.RecordSize is 0, DOS uses the default value of 128 bytes (one CP/M record).
    /// Applications can set RecordSize to any value (e.g., 512 for a sector, 4096 for a page) to work with
    /// different logical record sizes.
    /// </para>
    /// <para>
    /// <b>Division Method:</b> DOSBox and FreeDOS differ here:
    /// <list type="bullet">
    ///   <item><b>DOSBox</b> (dos_files.cpp:1650): Uses floor division: random = size / rec_size</item>
    ///   <item><b>FreeDOS</b> (fcbfns.c:307): Uses ceiling division: fcb_rndm = (fsize + (recsiz - 1)) / recsiz</item>
    /// </list>
    /// This implementation follows <b>DOSBox behavior (floor division)</b> for game compatibility.
    /// </para>
    /// <para>
    /// <b>Example:</b>
    /// <code>
    /// File size: 1000 bytes, RecordSize: 128
    /// DOSBox: 1000 / 128 = 7 records (floor)
    /// FreeDOS: (1000 + 127) / 128 = 8 records (ceiling)
    /// Spice86: 7 records (follows DOSBox)
    /// </code>
    /// FreeDOS's ceiling division ensures enough records to contain all bytes. DOSBox's floor division
    /// matches the actual number of complete records, which some games may rely on.
    /// </para>
    /// <para>
    /// <b>Usage Pattern:</b>
    /// <code>
    /// FCB.FileName = "MYFILE  "
    /// FCB.Extension = "DAT"
    /// FCB.RecordSize = 512  // Optional, defaults to 128
    /// Call INT 21h AH=23h
    /// // FCB.RandomRecord now contains file size in 512-byte records
    /// </code>
    /// </para>
    /// <para>
    /// <b>References:</b>
    /// <list type="bullet">
    ///   <item>DOSBox Staging: dos_files.cpp:1635-1651 DOS_FCBGetFileSize</item>
    ///   <item>FreeDOS kernel: fcbfns.c:285-316 FcbGetFileSize</item>
    ///   <item>Ralf Brown's Interrupt List: INT 21h AH=23h</item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <param name="fcbAddress">Linear address of the FCB containing the filename to query.</param>
    /// <returns><see cref="FcbStatus.Success"/> if file size retrieved, <see cref="FcbStatus.Error"/> if file not found or device.</returns>
    public FcbStatus GetFileSize(uint fcbAddress) {
        uint baseAddr = GetActualFcbBaseAddress(fcbAddress);
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
        uint baseAddr = GetActualFcbBaseAddress(fcbAddress);
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
        uint baseAddr = GetActualFcbBaseAddress(fcbAddress);
        DosFileControlBlock fcb = new DosFileControlBlock(_memory, baseAddr);

        string oldName = GetRenameOldName(fcb);
        string newPattern = GetRenameNewName(baseAddr);

        LogFcbDebug("RENAME START", baseAddr, $"Pattern: {oldName} -> {newPattern}", FcbStatus.Success);

        if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newPattern)) {
            LogFcbWarning("RENAME", baseAddr, "Missing old/new names");
            return FcbStatus.Error;
        }

        DosFileOperationResult ff = _dosFileManager.FindFirstMatchingFile(oldName, 0);
        if (ff.IsError) {
            LogFcbWarning("RENAME", baseAddr, $"FindFirst failed for pattern: {oldName}");
            return FcbStatus.Error;
        }

        // Collect all files to rename first (can't modify directory while enumerating)
        List<(string source, string dest)> filesToRename = new List<(string, string)>();
        HashSet<string> seenSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        HashSet<string> seenDestinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (true) {
            DosDiskTransferArea dta = new DosDiskTransferArea(_memory, MemoryUtils.ToPhysicalAddress(
                _dosFileManager.DiskTransferAreaAddressSegment, _dosFileManager.DiskTransferAreaAddressOffset));
            string sourceDos = dta.FileName;
            string destDos = ApplyWildcardRename(sourceDos, newPattern);

            LogFcbDebug("RENAME FILE", baseAddr, $"{sourceDos} -> {destDos}", FcbStatus.Success);

            string? srcHost = _dosFileManager.TryGetFullHostPathFromDos(sourceDos);
            if (string.IsNullOrWhiteSpace(srcHost)) {
                LogFcbWarning("RENAME", baseAddr, $"Unable to resolve source path: {sourceDos}");
                return FcbStatus.Error;
            }

            // Skip duplicates (same file with different case)
            if (seenSources.Contains(srcHost)) {
                LogFcbDebug("RENAME SKIP", baseAddr, $"Already collected: {srcHost}", FcbStatus.Success);
                DosFileOperationResult next = _dosFileManager.FindNextMatchingFile();
                if (next.IsError) {
                    break;
                }
                continue;
            }
            seenSources.Add(srcHost);

            // For destination, construct path in same directory as source
            string? srcDir = Path.GetDirectoryName(srcHost);
            if (string.IsNullOrWhiteSpace(srcDir)) {
                LogFcbWarning("RENAME", baseAddr, $"Unable to get source directory: {srcHost}");
                return FcbStatus.Error;
            }
            string dstHost = Path.Combine(srcDir, destDos);

            if (!File.Exists(srcHost)) {
                LogFcbWarning("RENAME", baseAddr, $"Source does not exist: {srcHost}");
                return FcbStatus.Error;
            }

            if (File.Exists(dstHost)) {
                LogFcbWarning("RENAME", baseAddr, $"Destination exists: {dstHost}");
                return FcbStatus.Error;
            }

            // Skip if this destination already appears in the rename list (wildcard conflict)
            if (seenDestinations.Contains(dstHost)) {
                LogFcbWarning("RENAME", baseAddr, $"Destination conflict in pattern: {dstHost}");
                return FcbStatus.Error;
            }
            seenDestinations.Add(dstHost);

            filesToRename.Add((srcHost, dstHost));

            DosFileOperationResult nx = _dosFileManager.FindNextMatchingFile();
            if (nx.IsError) {
                break;
            }
        }

        // Now rename all collected files
        int renameCount = 0;
        foreach ((string srcHost, string dstHost) in filesToRename) {
            LogFcbDebug("RENAME EXECUTE", baseAddr, $"Moving {srcHost} to {dstHost}", FcbStatus.Success);
            File.Move(srcHost, dstHost);
            renameCount++;
        }
        LogFcbDebug("RENAME", baseAddr, $"{oldName} -> {newPattern} ({renameCount} files)", FcbStatus.Success);
        return FcbStatus.Success;
    }

    public void SetRandomRecordNumber(uint fcbAddress) {
        uint fcbBase = GetActualFcbBaseAddress(fcbAddress);
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
        uint baseAddr = GetActualFcbBaseAddress(fcbAddress);
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
    /// Reads one record sequentially from an FCB-opened file.
    /// </summary>
    /// <param name="fcbAddress">Linear address of the FCB.</param>
    /// <param name="dtaAddress">Linear address of the DTA where data will be written.</param>
    /// <returns><see cref="FcbStatus"/> indicating success, EOF, or error.</returns>
    public FcbStatus ReadSequentialRecord(uint fcbAddress, uint dtaAddress) {
        return SequentialRead(fcbAddress, dtaAddress);
    }

    /// <summary>
    /// Writes one record sequentially to an FCB-opened file.
    /// </summary>
    /// <param name="fcbAddress">Linear address of the FCB.</param>
    /// <param name="dtaAddress">Linear address of the DTA containing data to write.</param>
    /// <returns><see cref="FcbStatus"/> indicating success or error.</returns>
    public FcbStatus WriteSequentialRecord(uint fcbAddress, uint dtaAddress) {
        return SequentialWrite(fcbAddress, dtaAddress);
    }

    /// <summary>
    /// Sets the random record field from the current block and record fields.
    /// FreeDOS: FcbSetRandom (fcbfns.c line 320).
    /// </summary>
    /// <param name="fcbAddress">Linear address of the FCB.</param>
    public void SetRandomRecord(uint fcbAddress) {
        uint baseAddr = GetActualFcbBaseAddress(fcbAddress);
        DosFileControlBlock fcb = new DosFileControlBlock(_memory, baseAddr);
        fcb.SetRandomFromPosition();
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

    private uint GetActualFcbBaseAddress(uint fcbAddress) {
        // Extended FCB detection via drive marker 0xFF
        DosFileControlBlock probe = new DosFileControlBlock(_memory, fcbAddress);
        if (probe.DriveNumber == 0xFF) {
            return fcbAddress + DosExtendedFileControlBlock.HeaderSize;
        }
        return fcbAddress;
    }

    private static string ConvertFcbPatternToDosPattern(string fcbName, string fcbExt) {
        // This implements FreeDOS ConvertName83ToNameSZ() behavior from fatdir.c
        // FCB fields are space-padded (8 chars name, 3 chars extension)
        // Convert to DOS pattern: "NAME    EXT" -> "NAME.EXT" or just "NAME" if no ext

        // Special case: '.' and '..' have no extension even if ext field is not empty
        bool noExtension = fcbName.Length > 0 && fcbName[0] == '.';

        // Trim trailing spaces from name
        int nameEnd = fcbName.Length - 1;
        while (nameEnd >= 0 && fcbName[nameEnd] == ' ') {
            nameEnd--;
        }
        string name = nameEnd >= 0 ? fcbName.Substring(0, nameEnd + 1) : "";

        if (noExtension) {
            return name;
        }

        // Trim trailing spaces from extension
        int extEnd = fcbExt.Length - 1;
        while (extEnd >= 0 && fcbExt[extEnd] == ' ') {
            extEnd--;
        }

        // Only add extension if there are non-space chars
        if (extEnd >= 0) {
            string ext = fcbExt.Substring(0, extEnd + 1);
            return $"{name}.{ext}";
        }

        return name;
    }

    private string GetRenameOldName(DosFileControlBlock fcb) {
        // Build DOS search pattern from FCB fields
        // DriveNumber: 0=default, 1=A, 2=B, etc.
        string pattern = ConvertFcbPatternToDosPattern(fcb.FileName, fcb.FileExtension);

        // Add drive prefix if specified
        if (fcb.DriveNumber > 0) {
            char driveLetter = (char)('A' + fcb.DriveNumber - 1);
            return $"{driveLetter}:{pattern}";
        }
        return pattern;
    }

    private string GetRenameNewName(uint fcbBaseAddress) {
        // Read new name and extension fields (space-padded, 8.3 format)
        string newNameField = ReadSpacePaddedField(fcbBaseAddress, RenameNewNameOffset, DosFileControlBlock.FileNameSize);
        string newExtField = ReadSpacePaddedField(fcbBaseAddress, RenameNewExtensionOffset, DosFileControlBlock.FileExtensionSize);

        // Keep the fields in their FCB format for wildcard processing
        // Don't trim yet - we need to preserve space-padding for proper substitution
        return $"{newNameField}|{newExtField}"; // Use delimiter to split later
    }

    private static string ApplyWildcardRename(string oldName, string newPatternWithDelimiter) {
        // Parse old name into 8.3 FCB format
        int dotOld = oldName.IndexOf('.');
        string oldBase = dotOld >= 0 ? oldName[..dotOld] : oldName;
        string oldExt = dotOld >= 0 ? oldName[(dotOld + 1)..] : string.Empty;

        // Pad to FCB sizes
        oldBase = oldBase.PadRight(DosFileControlBlock.FileNameSize);
        oldExt = oldExt.PadRight(DosFileControlBlock.FileExtensionSize);

        // Parse new pattern (uses | delimiter from GetRenameNewName)
        string[] parts = newPatternWithDelimiter.Split('|');
        string newNamePat = parts[0];
        string newExtPat = parts.Length > 1 ? parts[1] : "   ";

        // Apply character-by-character substitution
        // '?' means copy from old, anything else means use new char
        StringBuilder newName = new StringBuilder();
        for (int i = 0; i < DosFileControlBlock.FileNameSize; i++) {
            char patChar = i < newNamePat.Length ? newNamePat[i] : ' ';
            if (patChar == '?') {
                newName.Append(i < oldBase.Length ? oldBase[i] : ' ');
            } else {
                newName.Append(patChar);
            }
        }

        StringBuilder newExt = new StringBuilder();
        for (int i = 0; i < DosFileControlBlock.FileExtensionSize; i++) {
            char patChar = i < newExtPat.Length ? newExtPat[i] : ' ';
            if (patChar == '?') {
                newExt.Append(i < oldExt.Length ? oldExt[i] : ' ');
            } else {
                newExt.Append(patChar);
            }
        }

        // Trim and format result
        string finalName = newName.ToString().TrimEnd();
        string finalExt = newExt.ToString().TrimEnd();
        return string.IsNullOrEmpty(finalExt) ? finalName : $"{finalName}.{finalExt}";
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
        ulong offsetValue = absoluteRecord * (ulong)recordSize;
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

    /// <summary>
    /// Zero-pads the DTA buffer from the end of read data to the full record size.
    /// DOSBox Staging reference: dos_files.cpp:1494-1496.
    /// </summary>
    /// <param name="dtaAddress">Linear address of the DTA buffer.</param>
    /// <param name="bytesRead">Number of bytes actually read.</param>
    /// <param name="recordSize">Full record size to pad to.</param>
    private void ZeroPadDta(uint dtaAddress, int bytesRead, int recordSize) {
        for (int i = bytesRead; i < recordSize; i++) {
            _memory.UInt8[dtaAddress + (uint)i] = 0;
        }
    }

    /// <summary>
    /// Updates FCB size/date/time fields after a successful write operation.
    /// DOSBox Staging reference: dos_files.cpp:1526-1536.
    /// </summary>
    /// <param name="fcb">The FCB to update.</param>
    /// <param name="handle">The file handle.</param>
    /// <param name="offset">The file offset where write occurred.</param>
    /// <param name="bytesWritten">Number of bytes written.</param>
    private void UpdateFcbAfterWrite(DosFileControlBlock fcb, ushort handle, int offset, int bytesWritten) {
        // DOSBox: if (pos+towrite>size) size=pos+towrite; (dos_files.cpp:1528)
        uint newPosition = (uint)(offset + bytesWritten);
        if (newPosition > fcb.FileSize) {
            fcb.FileSize = newPosition;
        }
        // DOSBox: Update date/time (dos_files.cpp:1530-1531)
        UpdateFcbDateTime(fcb);
    }

    /// <summary>
    /// Updates FCB date/time fields to current time.
    /// DOSBox Staging reference: dos_files.cpp:1530-1531 DOS_GetBiosDatePacked/DOS_GetBiosTimePacked.
    /// </summary>
    /// <param name="fcb">The FCB to update.</param>
    private void UpdateFcbDateTime(DosFileControlBlock fcb) {
        DateTime now = DateTime.Now;
        fcb.Date = ToDosDate(now);
        fcb.Time = ToDosTime(now);
    }

    /// <summary>
    /// Converts a DateTime to DOS packed date format.
    /// DOS date format: bits 15-9=year-1980, bits 8-5=month, bits 4-0=day.
    /// </summary>
    private static ushort ToDosDate(DateTime localDate) {
        int day = localDate.Day;
        int month = localDate.Month;
        int dosYear = localDate.Year - 1980;
        return (ushort)((day & 0b11111) | (month & 0b1111) << 5 | (dosYear & 0b1111111) << 9);
    }

    /// <summary>
    /// Converts a DateTime to DOS packed time format.
    /// DOS time format: bits 15-11=hour, bits 10-5=minute, bits 4-0=seconds/2.
    /// </summary>
    private static ushort ToDosTime(DateTime localTime) {
        int dosSeconds = localTime.Second / 2;
        int minutes = localTime.Minute;
        int hours = localTime.Hour;
        return (ushort)((dosSeconds & 0b11111) | (minutes & 0b111111) << 5 | (hours & 0b11111) << 11);
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

