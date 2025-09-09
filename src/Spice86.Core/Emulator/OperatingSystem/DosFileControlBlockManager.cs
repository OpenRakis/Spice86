namespace Spice86.Core.Emulator.OperatingSystem;

using Serilog.Events;

using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System.IO;
using System.Text;

/// <summary>
/// Manages DOS File Control Block (FCB) operations for DOS functions 0x0F-0x24.
/// FCBs are the older file interface used by DOS 1.x and 2.x programs.
/// </summary>
public class DosFileControlBlockManager {
    private readonly IMemory _memory;
    private readonly DosFileManager _dosFileManager;
    private readonly DosDriveManager _dosDriveManager;
    private readonly ILoggerService _loggerService;

    /// <summary>
    /// Maps FCB addresses to file handles for open FCB files
    /// </summary>
    private readonly Dictionary<uint, ushort> _fcbToHandleMap = new();

    /// <summary>
    /// Maps file handles back to FCB addresses for cleanup
    /// </summary>
    private readonly Dictionary<ushort, uint> _handleToFcbMap = new();

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="dosFileManager">The DOS file manager for actual file operations.</param>
    /// <param name="dosDriveManager">The DOS drive manager.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public DosFileControlBlockManager(IMemory memory, DosFileManager dosFileManager,
        DosDriveManager dosDriveManager, ILoggerService loggerService) {
        _memory = memory;
        _dosFileManager = dosFileManager;
        _dosDriveManager = dosDriveManager;
        _loggerService = loggerService;
    }

    /// <summary>
    /// Opens a file using an FCB (DOS function 0x0F).
    /// </summary>
    /// <param name="fcbAddress">The physical address of the FCB in memory.</param>
    /// <returns>True if successful, false otherwise.</returns>
    public bool OpenFile(uint fcbAddress) {
        DosFileControlBlock fcb = new DosFileControlBlock(_memory, fcbAddress);

        if (string.IsNullOrWhiteSpace(fcb.FileName.Trim())) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("FCB Open: Invalid filename in FCB at 0x{FcbAddress:X8}", fcbAddress);
            }
            return false;
        }

        // Convert FCB filename to regular DOS filename
        string dosFileName = FcbFileNameToDosFileName(fcb);

        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("FCB Open: Opening file {FileName} from FCB at 0x{FcbAddress:X8}",
                dosFileName, fcbAddress);
        }

        // Try to open the file using the regular file manager
        DosFileOperationResult result = _dosFileManager.OpenFileOrDevice(dosFileName, FileAccessMode.ReadWrite);

        if (result.IsError) {
            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                _loggerService.Verbose("FCB Open: Failed to open file {FileName}", dosFileName);
            }
            return false;
        }

        ushort fileHandle = (ushort)result.Value!.Value;

        // Map the FCB to the file handle
        _fcbToHandleMap[fcbAddress] = fileHandle;
        _handleToFcbMap[fileHandle] = fcbAddress;

        // Update FCB with file information
        UpdateFcbFromFileInfo(fcb, dosFileName);

        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("FCB Open: Successfully opened {FileName} with handle {Handle}",
                dosFileName, fileHandle);
        }

        return true;
    }

    /// <summary>
    /// Closes a file using an FCB (DOS function 0x10).
    /// </summary>
    /// <param name="fcbAddress">The physical address of the FCB in memory.</param>
    /// <returns>True if successful, false otherwise.</returns>
    public bool CloseFile(uint fcbAddress) {
        if (!_fcbToHandleMap.TryGetValue(fcbAddress, out ushort fileHandle)) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("FCB Close: No open file found for FCB at 0x{FcbAddress:X8}", fcbAddress);
            }
            return false;
        }

        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("FCB Close: Closing file handle {Handle} for FCB at 0x{FcbAddress:X8}",
                fileHandle, fcbAddress);
        }

        DosFileOperationResult result = _dosFileManager.CloseFileOrDevice(fileHandle);

        // Clean up mappings regardless of result
        _fcbToHandleMap.Remove(fcbAddress);
        _handleToFcbMap.Remove(fileHandle);

        return !result.IsError;
    }

    /// <summary>
    /// Finds the first file matching the FCB pattern (DOS function 0x11).
    /// </summary>
    /// <param name="fcbAddress">The physical address of the FCB in memory.</param>
    /// <returns>True if a matching file was found, false otherwise.</returns>
    public bool FindFirstFile(uint fcbAddress) {
        DosFileControlBlock fcb = new DosFileControlBlock(_memory, fcbAddress);

        // Convert FCB pattern to DOS wildcard pattern
        string searchPattern = FcbPatternToDosPattern(fcb);

        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("FCB FindFirst: Searching for pattern {Pattern} from FCB at 0x{FcbAddress:X8}",
                searchPattern, fcbAddress);
        }

        // Use the regular file manager's find functionality
        DosFileOperationResult result = _dosFileManager.FindFirstMatchingFile(searchPattern, 0);

        if (result.IsError) {
            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                _loggerService.Verbose("FCB FindFirst: No files found matching pattern {Pattern}", searchPattern);
            }
            return false;
        }

        // Update the FCB in the DTA with the found file information
        UpdateFcbInDtaFromCurrentMatch();

        return true;
    }

    /// <summary>
    /// Finds the next file matching the FCB pattern (DOS function 0x12).
    /// </summary>
    /// <param name="fcbAddress">The physical address of the FCB in memory.</param>
    /// <returns>True if a matching file was found, false otherwise.</returns>
    public bool FindNextFile(uint fcbAddress) {
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("FCB FindNext: Continuing search from FCB at 0x{FcbAddress:X8}", fcbAddress);
        }

        DosFileOperationResult result = _dosFileManager.FindNextMatchingFile();

        if (result.IsError) {
            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                _loggerService.Verbose("FCB FindNext: No more files found");
            }
            return false;
        }

        // Update the FCB in the DTA with the found file information
        UpdateFcbInDtaFromCurrentMatch();

        return true;
    }

    /// <summary>
    /// Deletes a file using an FCB (DOS function 0x13).
    /// </summary>
    /// <param name="fcbAddress">The physical address of the FCB in memory.</param>
    /// <returns>True if successful, false otherwise.</returns>
    public bool DeleteFile(uint fcbAddress) {
        DosFileControlBlock fcb = new DosFileControlBlock(_memory, fcbAddress);

        string dosFileName = FcbFileNameToDosFileName(fcb);

        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("FCB Delete: Deleting file {FileName} from FCB at 0x{FcbAddress:X8}",
                dosFileName, fcbAddress);
        }

        DosFileOperationResult result = _dosFileManager.RemoveFile(dosFileName);

        return !result.IsError;
    }

    /// <summary>
    /// Reads a record from a file using an FCB (DOS functions 0x14, 0x21).
    /// </summary>
    /// <param name="fcbAddress">The physical address of the FCB in memory.</param>
    /// <param name="random">Whether this is a random access read (0x21) or sequential (0x14).</param>
    /// <returns>DOS error code: 0=success, 1=EOF, 2=segment wrap, 3=partial record.</returns>
    public byte ReadRecord(uint fcbAddress, bool random) {
        if (!_fcbToHandleMap.TryGetValue(fcbAddress, out ushort fileHandle)) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("FCB Read: No open file found for FCB at 0x{FcbAddress:X8}", fcbAddress);
            }
            return 0xFF; // File not open
        }

        DosFileControlBlock fcb = new DosFileControlBlock(_memory, fcbAddress);
        ushort recordSize = fcb.RecordSize == 0 ? (ushort)128 : fcb.RecordSize;

        // Calculate file position
        uint recordNumber = random ? fcb.RandomRecord :
            (uint)(fcb.CurrentBlock * 128 + fcb.CurrentRecord);

        uint filePosition = recordNumber * recordSize;

        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("FCB Read: Reading record {Record} ({Random}) from file handle {Handle}, position 0x{Position:X8}",
                recordNumber, random ? "random" : "sequential", fileHandle, filePosition);
        }

        // Seek to the correct position
        DosFileOperationResult seekResult = _dosFileManager.MoveFilePointerUsingHandle(
            SeekOrigin.Begin, fileHandle, filePosition);

        if (seekResult.IsError) {
            return 1; // EOF
        }

        // Read the record into the DTA
        uint dtaAddress = MemoryUtils.ToPhysicalAddress(
            _dosFileManager.DiskTransferAreaAddressSegment,
            _dosFileManager.DiskTransferAreaAddressOffset);

        DosFileOperationResult readResult = _dosFileManager.ReadFileOrDevice(
            fileHandle, recordSize, dtaAddress);

        if (readResult.IsError) {
            return 1; // EOF
        }

        ushort bytesRead = (ushort)readResult.Value!.Value;

        // Update FCB position for sequential reads
        if (!random) {
            fcb.CurrentRecord++;
            if (fcb.CurrentRecord >= 128) {
                fcb.CurrentRecord = 0;
                fcb.CurrentBlock++;
            }
        }

        // Return appropriate code
        if (bytesRead == 0) {
            return 1; // EOF
        } else if (bytesRead < recordSize) {
            return 3; // Partial record
        } else {
            return 0; // Success
        }
    }

    /// <summary>
    /// Writes a record to a file using an FCB (DOS functions 0x15, 0x22).
    /// </summary>
    /// <param name="fcbAddress">The physical address of the FCB in memory.</param>
    /// <param name="random">Whether this is a random access write (0x22) or sequential (0x15).</param>
    /// <returns>DOS error code: 0=success, 1=disk full, 2=segment wrap.</returns>
    public byte WriteRecord(uint fcbAddress, bool random) {
        if (!_fcbToHandleMap.TryGetValue(fcbAddress, out ushort fileHandle)) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("FCB Write: No open file found for FCB at 0x{FcbAddress:X8}", fcbAddress);
            }
            return 0xFF; // File not open
        }

        DosFileControlBlock fcb = new DosFileControlBlock(_memory, fcbAddress);
        ushort recordSize = fcb.RecordSize == 0 ? (ushort)128 : fcb.RecordSize;

        // Calculate file position
        uint recordNumber = random ? fcb.RandomRecord :
            (uint)(fcb.CurrentBlock * 128 + fcb.CurrentRecord);

        uint filePosition = recordNumber * recordSize;

        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("FCB Write: Writing record {Record} ({Random}) to file handle {Handle}, position 0x{Position:X8}",
                recordNumber, random ? "random" : "sequential", fileHandle, filePosition);
        }

        // Seek to the correct position
        DosFileOperationResult seekResult = _dosFileManager.MoveFilePointerUsingHandle(
            SeekOrigin.Begin, fileHandle, filePosition);

        if (seekResult.IsError) {
            return 1; // Disk full or error
        }

        // Write the record from the DTA
        uint dtaAddress = MemoryUtils.ToPhysicalAddress(
            _dosFileManager.DiskTransferAreaAddressSegment,
            _dosFileManager.DiskTransferAreaAddressOffset);

        DosFileOperationResult writeResult = _dosFileManager.WriteToFileOrDevice(
            fileHandle, recordSize, dtaAddress);

        if (writeResult.IsError) {
            return 1; // Disk full or error
        }

        // Update FCB position for sequential writes
        if (!random) {
            fcb.CurrentRecord++;
            if (fcb.CurrentRecord >= 128) {
                fcb.CurrentRecord = 0;
                fcb.CurrentBlock++;
            }
        }

        // Update file size in FCB
        uint newSize = filePosition + recordSize;
        if (newSize > fcb.FileSize) {
            fcb.FileSize = newSize;
        }

        return 0; // Success
    }

    /// <summary>
    /// Creates or truncates a file using an FCB (DOS function 0x16).
    /// </summary>
    /// <param name="fcbAddress">The physical address of the FCB in memory.</param>
    /// <returns>True if successful, false otherwise.</returns>
    public bool CreateFile(uint fcbAddress) {
        DosFileControlBlock fcb = new DosFileControlBlock(_memory, fcbAddress);

        string dosFileName = FcbFileNameToDosFileName(fcb);

        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("FCB Create: Creating file {FileName} from FCB at 0x{FcbAddress:X8}",
                dosFileName, fcbAddress);
        }

        DosFileOperationResult result = _dosFileManager.CreateFileUsingHandle(dosFileName, 0);

        if (result.IsError) {
            return false;
        }

        ushort fileHandle = (ushort)result.Value!.Value;

        // Map the FCB to the file handle
        _fcbToHandleMap[fcbAddress] = fileHandle;
        _handleToFcbMap[fileHandle] = fcbAddress;

        // Initialize FCB for new file
        fcb.CurrentBlock = 0;
        fcb.CurrentRecord = 0;
        fcb.FileSize = 0;
        if (fcb.RecordSize == 0) {
            fcb.RecordSize = 128;
        }

        return true;
    }

    /// <summary>
    /// Renames a file using an FCB (DOS function 0x17).
    /// </summary>
    /// <param name="fcbAddress">The physical address of the FCB in memory containing both old and new names.</param>
    /// <returns>True if successful, false otherwise.</returns>
    public bool RenameFile(uint fcbAddress) {
        // For rename, the FCB contains the old name in the first 11 bytes and new name in bytes 17-27
        byte[] fcbData = _memory.GetData(fcbAddress, 37);

        // Extract old filename (bytes 1-11)
        string oldName = ExtractFilenameFromFcbBytes(fcbData, 1);

        // Extract new filename (bytes 17-27) 
        string newName = ExtractFilenameFromFcbBytes(fcbData, 17);

        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("FCB Rename: Renaming {OldName} to {NewName} from FCB at 0x{FcbAddress:X8}",
                oldName, newName, fcbAddress);
        }

        // DOS doesn't have a direct rename function in the file manager, so we need to 
        // implement this by copying the file and deleting the original
        string? oldHostPath = _dosFileManager.TryGetFullHostPathFromDos(oldName);

        if (string.IsNullOrEmpty(oldHostPath) || !File.Exists(oldHostPath)) {
            return false;
        }

        string newHostPath = Path.Join(Path.GetDirectoryName(oldHostPath)!, newName);
        try {
            File.Move(oldHostPath, newHostPath);
            return true;
        } catch (Exception ex) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning(ex, "FCB Rename: Failed to rename {OldName} to {NewName}", oldName, newName);
            }
            return false;
        }
    }

    /// <summary>
    /// Converts an FCB filename to a DOS filename string.
    /// </summary>
    private static string FcbFileNameToDosFileName(DosFileControlBlock fcb) {
        string name = fcb.FileName.TrimEnd();
        string ext = fcb.FileExtension.TrimEnd();

        if (string.IsNullOrEmpty(ext)) {
            return name;
        }

        return $"{name}.{ext}";
    }

    /// <summary>
    /// Converts an FCB search pattern to a DOS wildcard pattern.
    /// </summary>
    private static string FcbPatternToDosPattern(DosFileControlBlock fcb) {
        string name = fcb.FileName.Replace('?', '?').TrimEnd();
        string ext = fcb.FileExtension.Replace('?', '?').TrimEnd();

        // Convert spaces to wildcards for incomplete patterns
        if (name.Contains(' ')) {
            name = name.TrimEnd() + "*";
        }

        if (ext.Contains(' ')) {
            ext = ext.TrimEnd() + "*";
        }

        if (string.IsNullOrEmpty(ext) || ext == "*") {
            return string.IsNullOrEmpty(name) ? "*.*" : $"{name}.*";
        }

        return $"{name}.{ext}";
    }

    /// <summary>
    /// Updates FCB with file information from the file system.
    /// </summary>
    private void UpdateFcbFromFileInfo(DosFileControlBlock fcb, string dosFileName) {
        string? hostPath = _dosFileManager.TryGetFullHostPathFromDos(dosFileName);

        if (string.IsNullOrEmpty(hostPath) || !File.Exists(hostPath)) {
            return;
        }

        try {
            FileInfo fileInfo = new FileInfo(hostPath);
            fcb.FileSize = (uint)fileInfo.Length;

            // Convert to DOS date/time format
            DateTime localTime = fileInfo.LastWriteTime;
            fcb.LastWriteDate = ToDosDate(localTime);
            fcb.LastWriteTime = ToDosTime(localTime);

            // Initialize other fields
            fcb.CurrentBlock = 0;
            fcb.CurrentRecord = 0;
            if (fcb.RecordSize == 0) {
                fcb.RecordSize = 128;
            }
        } catch (Exception ex) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning(ex, "Failed to get file info for {FileName}", dosFileName);
            }
        }
    }

    /// <summary>
    /// Updates the FCB in the DTA from the current file match.
    /// </summary>
    private void UpdateFcbInDtaFromCurrentMatch() {
        // Get the DTA
        uint dtaAddress = MemoryUtils.ToPhysicalAddress(
            _dosFileManager.DiskTransferAreaAddressSegment,
            _dosFileManager.DiskTransferAreaAddressOffset);

        DosDiskTransferArea dta = new DosDiskTransferArea(_memory, dtaAddress);

        // Create an FCB at the beginning of the DTA for the found file
        // FCB format in DTA: drive byte + 8.3 filename + attributes + file info
        string fileName = dta.FileName;

        // Parse the 8.3 filename
        string[] parts = fileName.Split('.');
        string name = parts[0].PadRight(8);
        string ext = parts.Length > 1 ? parts[1].PadRight(3) : "   ";

        // Write the FCB data to the DTA
        _memory.UInt8[dtaAddress + 0] = 0; // Drive (0 = default)

        // Write filename (8 bytes)
        byte[] nameBytes = Encoding.ASCII.GetBytes(name.Substring(0, 8));
        for (int i = 0; i < 8; i++) {
            _memory.UInt8[dtaAddress + 1 + i] = nameBytes[i];
        }

        // Write extension (3 bytes) 
        byte[] extBytes = Encoding.ASCII.GetBytes(ext.Substring(0, 3));
        for (int i = 0; i < 3; i++) {
            _memory.UInt8[dtaAddress + 9 + i] = extBytes[i];
        }
    }

    /// <summary>
    /// Extracts a filename from FCB bytes at the specified offset.
    /// </summary>
    private static string ExtractFilenameFromFcbBytes(byte[] fcbData, int offset) {
        // Extract 8.3 filename from FCB format
        string name = Encoding.ASCII.GetString(fcbData, offset, 8).TrimEnd();
        string ext = Encoding.ASCII.GetString(fcbData, offset + 8, 3).TrimEnd();

        if (string.IsNullOrEmpty(ext)) {
            return name;
        }

        return $"{name}.{ext}";
    }

    private static ushort ToDosDate(DateTime localDate) {
        int day = localDate.Day;
        int month = localDate.Month;
        int dosYear = localDate.Year - 1980;
        return (ushort)((day & 0b11111) | (month & 0b1111) << 5 | (dosYear & 0b1111111) << 9);
    }

    private static ushort ToDosTime(DateTime localTime) {
        int dosSeconds = localTime.Second / 2;
        int minutes = localTime.Minute;
        int hours = localTime.Hour;
        return (ushort)((dosSeconds & 0b11111) | (minutes & 0b111111) << 5 | (hours & 0b11111) << 11);
    }
}