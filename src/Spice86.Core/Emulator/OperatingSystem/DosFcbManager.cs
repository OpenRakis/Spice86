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
/// Implements legacy CP/M-style FCB operations required by many DOS programs and games.
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

    private const int RenameNewNameOffset = 0x11;
    private const int RenameNewExtensionOffset = 0x19;

    /// <summary>
    /// Common separator characters for filename parsing.
    /// These characters can appear between components of a DOS path and may be skipped during parsing.
    /// </summary>
    public const string CommonSeparators = ":;,=+ \t";

    /// <summary>
    /// Field separator characters that cannot appear in filename or extension.
    /// These characters terminate filename parsing and are never part of a valid DOS filename.
    /// </summary>
    public const string FieldSeparators = "/\\\"[]<>|.:;,=+\t";

    private readonly IMemory _memory;
    private readonly DosFileManager _dosFileManager;
    private readonly DosDriveManager _dosDriveManager;
    private readonly ILoggerService _loggerService;
    private readonly DosSwappableDataArea _sda;

    // Track FCB handles per PSP: handle → PSP segment
    private readonly Dictionary<ushort, ushort> _trackedFcbHandles = new();

    public
    DosFcbManager(IMemory memory, DosFileManager dosFileManager, DosDriveManager dosDriveManager, ILoggerService loggerService) {
        _memory = memory;
        _dosFileManager = dosFileManager;
        _dosDriveManager = dosDriveManager;
        _loggerService = loggerService;
        _sda = new DosSwappableDataArea(_memory,
            MemoryUtils.ToPhysicalAddress(DosSwappableDataArea.BaseSegment, 0));
    }

    /// <summary>
    /// INT 21h, AH=29h - Parse filename into an FCB.
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
        if (parseControl.HasFlag(FcbParseControl.SkipLeadingSeparators)) {
            while (pos < filename.Length && TestCmnSeps(filename[pos])) {
                pos++;
            }
        }

        // Skip whitespace (undocumented: "Undocumented 'feature,' we skip white space anyway")
        pos = ParseSkipWh(filename, pos);

        // Check for drive specification
        if (pos + 1 < filename.Length && filename[pos + 1] == ':' && !TestFieldSeps(filename[pos])) {
            char driveChar = char.ToUpper(filename[pos]);
            if (driveChar is >= 'A' and <= 'Z') {
                byte driveNum = (byte)(driveChar - 'A');

                // Undocumented behavior: should keep parsing even if drive is invalid
                if (!_dosDriveManager.HasDriveAtIndex(driveNum)) {
                    retCodeDrive = true;
                }

                fcb.DriveNumber = (byte)(driveNum + 1);
                pos += 2;
            }
        } else if (!parseControl.HasFlag(FcbParseControl.LeaveDriveUnchanged)) {
            // If flag NOT set, set to default drive (0)
            fcb.DriveNumber = 0;
        }

        /* Undocumented behavior, set record number & record size to 0  */
        /* per MS-DOS Encyclopedia pp269 no other FCB fields modified   */
        /* except zeroing current block and record size fields          */
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
        fcb.FileName = ExtractAndPadField(filename, nameStart, pos, 8);

        // Parse extension if present
        if (pos < filename.Length && filename[pos] == '.') {
            pos++;
            int extStart = pos;
            (pos, retCodeExt) = GetNameField(filename, pos, 3);
            fcb.FileExtension = ExtractAndPadField(filename, extStart, pos, 3);
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
    /// </summary>
    private static bool TestCmnSeps(char c) {
        return CommonSeparators.Contains(c);
    }

    /// <summary>
    /// Checks if character is a field separator.
    /// </summary>
    private static bool TestFieldSeps(char c) {
        return c <= ' ' || FieldSeparators.Contains(c);
    }

    /// <summary>
    /// Skip whitespace.
    /// </summary>
    private static int ParseSkipWh(string filename, int pos) {
        while (pos < filename.Length && (filename[pos] == ' ' || filename[pos] == '\t')) {
            pos++;
        }
        return pos;
    }

    /// <summary>
    /// Parse a name field (filename or extension).
    /// Returns a tuple of (newPos, hasWildcard).
    /// </summary>
    private static (int newPos, bool hasWildcard) GetNameField(string filename, int pos, int fieldSize) {
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

        // After reading fieldSize characters, continue advancing until we hit a separator
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
    private static string ExtractAndPadField(string filename, int startPos, int endPos, int fieldSize) {
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
        // Always set RecordSize to 128 and reset position on open
        fcb.RecordSize = DosFileControlBlock.DefaultRecordSize;
        fcb.CurrentBlock = 0;
        fcb.CurrentRecord = 0;

        // Populate FCB metadata (size, date, time) on open
        VirtualFileBase? vf = _dosFileManager.OpenFiles[handle];
        if (vf is DosFile dosFile) {
            fcb.FileSize = (uint)dosFile.Length;

            // Get file's last write time from the file system
            string? hostPath = _dosFileManager.TryGetFullHostPathFromDos(fileSpec);
            if (!string.IsNullOrWhiteSpace(hostPath) && File.Exists(hostPath)) {
                FileInfo fileInfo = new FileInfo(hostPath);
                DateTime lastWrite = fileInfo.LastWriteTime;
                fcb.Date = DosFileManager.ToDosDate(lastWrite);
                fcb.Time = DosFileManager.ToDosTime(lastWrite);

                // Also update the DosFile object for consistency
                dosFile.Date = fcb.Date;
                dosFile.Time = fcb.Time;
            }
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
        uint baseAddr = GetActualFcbBaseAddress(fcbAddress);
        DosFileControlBlock fcb = new DosFileControlBlock(_memory, baseAddr);
        ushort handle = fcb.SftNumber;
        if (handle == 0) {
            LogFcbWarning("CLOSE", baseAddr, "Handle is zero");
            return FcbStatus.Error;
        }
        // Set handle to 0xFF before closing; enables auto-reopen in subsequent read/write
        fcb.SftNumber = 0xFF;
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
        // Always set RecordSize to 128 on create
        fcb.RecordSize = DosFileControlBlock.DefaultRecordSize;
        TrackFcbHandle(handle);
        LogFcbDebug("CREATE", baseAddr, fileSpec, FcbStatus.Success);
        return FcbStatus.Success;
    }

    /// <summary>
    /// INT 21h AH=14h sequential read using the current block/record pointer.
    /// </summary>
    /// <remarks>
    /// Auto-reopens closed FCBs if handle=0xFF but rec_size!=0.
    /// Zero-pads partial reads to full record size.
    /// Advances block/record position after read.
    /// </remarks>
    /// <param name="fcbAddress">Linear address of the FCB.</param>
    /// <param name="dtaAddress">Linear address of the DTA buffer.</param>
    /// <returns><see cref="FcbStatus"/> describing the outcome.</returns>
    public FcbStatus SequentialRead(uint fcbAddress, uint dtaAddress) {
        uint baseAddr = GetActualFcbBaseAddress(fcbAddress);
        DosFileControlBlock fcb = new DosFileControlBlock(_memory, baseAddr);
        ushort handle = fcb.SftNumber;

        // Auto-reopen closed FCB if handle=0xFF but rec_size!=0
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

        // Zero-pad partial reads to full record size
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
    /// Auto-reopens closed FCBs if handle=0xFF but rec_size!=0.
    /// Updates FCB size/date/time after successful write.
    /// Advances block/record position after write.
    /// </remarks>
    /// <param name="fcbAddress">Linear address of the FCB.</param>
    /// <param name="dtaAddress">Linear address of the DTA buffer.</param>
    /// <returns><see cref="FcbStatus"/> describing the outcome.</returns>
    public FcbStatus SequentialWrite(uint fcbAddress, uint dtaAddress) {
        uint baseAddr = GetActualFcbBaseAddress(fcbAddress);
        DosFileControlBlock fcb = new DosFileControlBlock(_memory, baseAddr);
        ushort handle = fcb.SftNumber;

        // Auto-reopen closed FCB if handle=0xFF but rec_size!=0
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

        // Update FCB size/date/time after successful write
        UpdateFcbAfterWrite(fcb, offset, len);

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
    /// Sets block/record from random field before read.
    /// For single-record read (AH=21h), restores block/record after read.
    /// Zero-pads partial reads.
    /// </remarks>
    /// <param name="fcbAddress">Linear address of the FCB.</param>
    /// <param name="dtaAddress">Linear address of the DTA buffer.</param>
    /// <returns><see cref="FcbStatus"/> describing the outcome.</returns>
    public FcbStatus RandomRead(uint fcbAddress, uint dtaAddress) {
        uint baseAddr = GetActualFcbBaseAddress(fcbAddress);
        DosFileControlBlock fcb = new DosFileControlBlock(_memory, baseAddr);
        ushort handle = fcb.SftNumber;

        // Auto-reopen closed FCB if handle=0xFF but rec_size!=0
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

        // Set block/record from random field
        // Random read positions the FCB at the random record location
        fcb.CalculateRecordPosition();

        // Save position for restore after read.
        // SequentialRead advances position, so we restore to the random position afterward.
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

        // Zero-pad partial reads to full record size
        if (len > 0 && len < recordSize) {
            ZeroPadDta(dtaAddress, len, recordSize);
        }

        // Restore position to random location
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
    /// Sets block/record from random field before write.
    /// For single-record write (AH=22h), restores block/record after write.
    /// Updates FCB size/date/time after write.
    /// </remarks>
    /// <param name="fcbAddress">Linear address of the FCB.</param>
    /// <param name="dtaAddress">Linear address of the DTA buffer.</param>
    /// <returns><see cref="FcbStatus"/> describing the outcome.</returns>
    public FcbStatus RandomWrite(uint fcbAddress, uint dtaAddress) {
        uint baseAddr = GetActualFcbBaseAddress(fcbAddress);
        DosFileControlBlock fcb = new DosFileControlBlock(_memory, baseAddr);
        ushort handle = fcb.SftNumber;

        // Auto-reopen closed FCB if handle=0xFF but rec_size!=0
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

        // Set block/record from random field
        fcb.CalculateRecordPosition();

        // Store old block/record for restore after write
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

        // Update FCB size/date/time after successful write
        UpdateFcbAfterWrite(fcb, offset, len);

        // Restore old block/record for single-record random write
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
    /// Sets block/record from random field.
    /// Loops per-record, advancing the DTA address for each.
    /// Updates block/record and RandomRecord after read.
    /// Zero-pads partial reads.
    /// </remarks>
    /// <param name="fcbAddress">Linear address of the FCB.</param>
    /// <param name="dtaAddress">Linear address of the DTA buffer.</param>
    /// <param name="recordCount">On input, requested record count; on output, number actually read.</param>
    /// <returns><see cref="FcbStatus"/> describing the outcome.</returns>
    public FcbStatus RandomBlockRead(uint fcbAddress, uint dtaAddress, ref ushort recordCount) {
        uint baseAddr = GetActualFcbBaseAddress(fcbAddress);
        DosFileControlBlock fcb = new DosFileControlBlock(_memory, baseAddr);
        ushort handle = fcb.SftNumber;

        // Auto-reopen closed FCB if handle=0xFF but rec_size!=0
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

        // Set block/record from random field
        fcb.CalculateRecordPosition();

        ushort totalRead = 0;
        FcbStatus lastError = FcbStatus.Success;

        // Loop for each record
        for (ushort i = 0; i < recordCount; i++) {
            uint absoluteRecord = startRecord + i;
            if (!TryComputeOffset(absoluteRecord, recordSize, out int offset, out FcbStatus offsetStatus)) {
                lastError = offsetStatus;
                break;
            }
            DosFileOperationResult seek = _dosFileManager.MoveFilePointerUsingHandle(SeekOrigin.Begin, handle, offset);
            if (seek.IsError) {
                lastError = FcbStatus.NoData;
                break;
            }
            // Advance DTA destination for each record
            uint destinationAddress = dtaAddress + totalRead * (uint)recordSize;
            DosFileOperationResult read = _dosFileManager.ReadFileOrDevice(handle, (ushort)recordSize, destinationAddress);
            if (read.IsError) {
                lastError = FcbStatus.NoData;
                break;
            }
            ushort len = (ushort)(read.Value ?? 0);
            if (len == 0) {
                lastError = FcbStatus.NoData;
                break;
            }

            // Zero-pad partial reads to full record size
            if (len < recordSize) {
                ZeroPadDta(destinationAddress, len, recordSize);
            }

            totalRead++;

            // Advance block/record after each read
            if (++fcb.CurrentRecord > 127) {
                fcb.CurrentRecord = 0;
                fcb.CurrentBlock++;
            }

            if (len < recordSize) {
                lastError = FcbStatus.EndOfFile;
                break;
            }
        }

        // Update RandomRecord with new position
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
    /// Sets block/record from random field.
    /// Special handling for CX=0: truncate/extend file.
    /// Loops per-record, reading from DTA at record offsets.
    /// Updates block/record and RandomRecord after write.
    /// </remarks>
    /// <param name="fcbAddress">Linear address of the FCB.</param>
    /// <param name="dtaAddress">Linear address of the DTA buffer.</param>
    /// <param name="recordCount">On input, requested record count; on output, number actually written.</param>
    /// <returns><see cref="FcbStatus"/> describing the outcome.</returns>
    public FcbStatus RandomBlockWrite(uint fcbAddress, uint dtaAddress, ref ushort recordCount) {
        uint baseAddr = GetActualFcbBaseAddress(fcbAddress);
        DosFileControlBlock fcb = new DosFileControlBlock(_memory, baseAddr);
        ushort handle = fcb.SftNumber;

        // Auto-reopen closed FCB if handle=0xFF but rec_size!=0
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

        // Set block/record from random field
        fcb.CalculateRecordPosition();

        if (recordCount == 0) {
            // Truncate/extend file to random record position
            if (!TryComputeOffset(startRecord, recordSize, out int offset, out FcbStatus offsetStatus)) {
                return offsetStatus;
            }
            VirtualFileBase? vf = _dosFileManager.OpenFiles[handle];
            if (vf is DosFile dosFile) {
                dosFile.SetLength(offset);
                // Update size/date/time
                fcb.FileSize = (uint)offset;
                UpdateFcbDateTime(fcb);
                LogFcbDebug("RAND BLK WRITE", baseAddr, fcb.FullFileName, FcbStatus.Success);
                return FcbStatus.Success;
            }
            LogFcbWarning("RAND BLK WRITE", baseAddr, "Handle is not a file");
            return FcbStatus.Error;
        }

        ushort totalWritten = 0;
        FcbStatus lastError = FcbStatus.Success;

        // Loop for each record
        for (ushort i = 0; i < recordCount; i++) {
            uint absoluteRecord = startRecord + i;
            if (!TryComputeOffset(absoluteRecord, recordSize, out int offset, out FcbStatus offsetStatus)) {
                lastError = offsetStatus;
                break;
            }
            DosFileOperationResult seek = _dosFileManager.MoveFilePointerUsingHandle(SeekOrigin.Begin, handle, offset);
            if (seek.IsError) {
                lastError = FcbStatus.NoData;
                break;
            }

            // Read from DTA at record offset
            uint sourceAddress = dtaAddress + i * (uint)recordSize;
            DosFileOperationResult write = _dosFileManager.WriteToFileOrDevice(handle, (ushort)recordSize, sourceAddress);
            if (write.IsError) {
                lastError = FcbStatus.NoData;
                break;
            }
            ushort len = (ushort)(write.Value ?? 0);

            // Update FCB size/date/time after successful write
            UpdateFcbAfterWrite(fcb, offset, len);

            totalWritten++;

            // Advance block/record after each write
            if (++fcb.CurrentRecord > 127) {
                fcb.CurrentRecord = 0;
                fcb.CurrentBlock++;
            }

            if (len < recordSize) {
                break;
            }
        }

        // Update RandomRecord with new position
        fcb.RandomRecord = (uint)fcb.CurrentBlock * 128 + fcb.CurrentRecord;

        recordCount = totalWritten;
        if (totalWritten == 0) {
            return FcbStatus.NoData;
        }
        if (lastError != FcbStatus.Success) {
            return lastError;
        }
        LogFcbDebug("RAND BLK WRITE", baseAddr, fcb.FullFileName, FcbStatus.Success);
        return FcbStatus.Success;
    }

    /// <summary>
    /// INT 21h AH=23h - Get File Size in Records.
    /// Opens the file, divides size by record size using ceiling division, stores result in FCB.RandomRecord,
    /// then closes the file. If RecordSize is 0, uses default 128 bytes.
    /// </summary>
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
            if (size % recSize != 0) {
                records++;
            }
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
            return FcbStatus.Error;
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
            string dstHost = Path.Join(srcDir, destDos);

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
            try {
                File.Move(srcHost, dstHost);
                renameCount++;
            } catch (IOException ex) {
                LogFcbWarning("RENAME", baseAddr, $"Failed to rename {srcHost}: {ex.Message}");
                return FcbStatus.Error;
            } catch (UnauthorizedAccessException ex) {
                LogFcbWarning("RENAME", baseAddr, $"Access denied renaming {srcHost}: {ex.Message}");
                return FcbStatus.Error;
            }
        }
        LogFcbDebug("RENAME", baseAddr, $"{oldName} -> {newPattern} ({renameCount} files)", FcbStatus.Success);
        return FcbStatus.Success;
    }

    /// <summary>
    /// INT 21h AH=11h FCB Find First using the FCB filename pattern.
    /// </summary>
    /// <param name="fcbAddress">Linear address of the FCB.</param>
    /// <returns><see cref="FcbStatus"/> describing the outcome.</returns>
    public FcbStatus FindFirst(uint fcbAddress) {
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
    /// <returns><see cref="FcbStatus"/> describing the outcome.</returns>
    public FcbStatus FindNext() {
        DosFileOperationResult result = _dosFileManager.FindNextMatchingFile();
        FcbStatus status = result.IsError ? FcbStatus.Error : FcbStatus.Success;
        LogFcbDebug("FIND NEXT", 0, string.Empty, status);
        return status;
    }

    /// <summary>
    /// INT 21h AH=24h - Sets the random record field from the current block and record fields.
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
    /// Closes all FCB-opened files for the specified PSP segment.
    /// Only closes FCB files owned by the terminating process.
    /// </summary>
    /// <param name="pspSegment">The PSP segment of the process being terminated.</param>
    public void CloseAllTrackedFcbFiles(ushort pspSegment) {
        List<ushort> handlesToClose = _trackedFcbHandles
            .Where(entry => entry.Value == pspSegment)
            .Select(entry => entry.Key)
            .ToList();

        // Close the files
        foreach (ushort handle in handlesToClose) {
            DosFileOperationResult result = _dosFileManager.CloseFileOrDevice(handle);
            if (result.IsError && _loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("Failed to close FCB handle {Handle} for PSP {Psp}", handle, pspSegment);
            }
            _trackedFcbHandles.Remove(handle);
        }
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

    /// <summary>
    /// Tracks an FCB handle for the current PSP so it can be closed on process termination.
    /// </summary>
    private void TrackFcbHandle(ushort handle) {
        ushort currentPsp = _sda.CurrentProgramSegmentPrefix;
        _trackedFcbHandles[handle] = currentPsp;
    }

    /// <summary>
    /// Zero-pads the DTA buffer from the end of read data to the full record size.
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
    /// </summary>
    /// <param name="fcb">The FCB to update.</param>
    /// <param name="offset">The file offset where write occurred.</param>
    /// <param name="bytesWritten">Number of bytes written.</param>
    private void UpdateFcbAfterWrite(DosFileControlBlock fcb, int offset, int bytesWritten) {
        uint newPosition = (uint)(offset + bytesWritten);
        if (newPosition > fcb.FileSize) {
            fcb.FileSize = newPosition;
        }
        UpdateFcbDateTime(fcb);
    }

    /// <summary>
    /// Updates FCB date/time fields to current time.
    /// </summary>
    /// <param name="fcb">The FCB to update.</param>
    private void UpdateFcbDateTime(DosFileControlBlock fcb) {
        DateTime now = DateTime.Now;
        fcb.Date = DosFileManager.ToDosDate(now);
        fcb.Time = DosFileManager.ToDosTime(now);
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