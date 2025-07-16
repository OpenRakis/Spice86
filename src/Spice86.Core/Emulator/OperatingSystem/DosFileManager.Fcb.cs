namespace Spice86.Core.Emulator.OperatingSystem;

using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System.IO;

public partial class DosFileManager {
    private readonly DosSwappableDataArea _dosSwappableDataArea;
   
    // Constants for FCB operations
    private const byte FCB_SUCCESS = 0;
    private const byte FCB_READ_NODATA = 1;
    private const byte FCB_READ_PARTIAL = 3;
    private const byte FCB_ERR_NODATA = 1;
    private const byte FCB_ERR_EOF = 3;
    private const byte FCB_ERR_WRITE = 1;

    // Update these fields to reference the DosSwappableDataArea
    
    // FCB fields offset constants within the SDA
    private const ushort SDA_OFS = 0x0C00;
    private const ushort FCB_FIND_OFFSET = 0x0080;
    private const ushort FCB_DELETE_OFFSET = 0x0180;

    /// <summary>
    /// Segment for temporary DTA used during FCB find operations.
    /// </summary>
    internal ushort TemporaryDiskTransferAreaSegment => DosSwappableDataArea.BaseSegment;

    /// <summary>
    /// Offset for temporary DTA used during FCB find operations.
    /// </summary>
    internal ushort TemporaryDiskTransferAreaOffset => SDA_OFS + FCB_FIND_OFFSET;

    /// <summary>
    /// Segment for temporary DTA used during FCB delete operations.
    /// </summary>
    internal ushort TemporaryDeleteFcbSegment => DosSwappableDataArea.BaseSegment;

    /// <summary>
    /// Offset for temporary DTA used during FCB delete operations.
    /// </summary>
    internal ushort TemporaryDeleteFcbOffset => SDA_OFS + FCB_DELETE_OFFSET;

    // SDA constants - mirroring what would typically be defined in the DOS environment
    private const ushort DOS_SDA_SEG = DosSwappableDataArea.BaseSegment;
    private const ushort DOS_SDA_OFS = 0x0C00;

    private readonly DosFileControlBlockParser _fcbParser;

    /// <summary>
    /// Opens a file using FCB at the specified memory location.
    /// </summary>
    /// <param name="segment">The segment of the FCB.</param>
    /// <param name="offset">The offset of the FCB.</param>
    /// <returns>true if successful, false otherwise.</returns>
    public bool OpenFcb(ushort segment, ushort offset) {
        DosFileControlBlock fcb = new(_memory, MemoryUtils.ToPhysicalAddress(segment, offset));
        string shortname = _fcbParser.GetFcbName(fcb);
        
        // If filename contains wildcards, we need to find first matching file
        if (shortname.Contains('*') || shortname.Contains('?')) {
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Debug)) {
                _loggerService.Debug("FCB: Wildcards in filename {Filename}", shortname);
            }
            
            if (!FindFirstFcb(segment, offset)) {
                return false;
            }
            
            // Get the found file from the DTA and update the FCB
            DosDiskTransferArea dta = new(_memory, MemoryUtils.ToPhysicalAddress(DiskTransferAreaAddressSegment, DiskTransferAreaAddressOffset));
            DosFileControlBlock findFcb = new(_memory, MemoryUtils.ToPhysicalAddress(DiskTransferAreaAddressSegment, DiskTransferAreaAddressOffset));
            
            var foundFileName = findFcb.FileName;
            var foundExt = findFcb.Extension;
            
            fcb.FileName = foundFileName;
            fcb.Extension = foundExt;
            
            shortname = _fcbParser.GetFcbName(fcb);
        }
        
        // Check if file exists
        string? hostPath = TryGetFullHostPathFromDos(shortname);
        if (string.IsNullOrEmpty(hostPath)) {
            return false;
        }
        
        // Open file
        DosFileOperationResult result = OpenFileInternal(shortname, hostPath, FileAccessMode.ReadWrite);
        if (result.IsError) {
            return false;
        }
        
        ushort handle = (ushort)result.Value!.Value;
        FileOpenFcb(fcb, (byte)handle);
        return true;
    }
    
    /// <summary>
    /// Creates a file using FCB at the specified memory location.
    /// </summary>
    /// <param name="segment">The segment of the FCB.</param>
    /// <param name="offset">The offset of the FCB.</param>
    /// <returns>true if successful, false otherwise.</returns>
    public bool CreateFcb(ushort segment, ushort offset) {
        DosFileControlBlock fcb = new(_memory, MemoryUtils.ToPhysicalAddress(segment, offset));
        string shortname = _fcbParser.GetFcbName(fcb);
        
        // Check for extended FCB to get attributes
        DosFileAttributes attr = DosFileAttributes.Archive;
        if (fcb.Drive == 0xFF) {
            DosExtendedFileControlBlock extFcb = new(fcb);
            attr = (DosFileAttributes)extFcb.FileAttribute;
            // Don't allow directory creation
            if ((attr & DosFileAttributes.Directory) != 0) {
                return false;
            }
        }
        
        // Create file
        DosFileOperationResult result = CreateFileUsingHandle(shortname, (ushort)attr);
        if (result.IsError) {
            return false;
        }
        
        ushort handle = (ushort)result.Value!.Value;
        FileOpenFcb(fcb, (byte)handle);
        return true;
    }
    
    /// <summary>
    /// Closes a file using FCB at the specified memory location.
    /// </summary>
    /// <param name="segment">The segment of the FCB.</param>
    /// <param name="offset">The offset of the FCB.</param>
    /// <returns>true if successful, false otherwise.</returns>
    public bool CloseFcb(ushort segment, ushort offset) {
        DosFileControlBlock fcb = new(_memory, MemoryUtils.ToPhysicalAddress(segment, offset));
        
        if (!fcb.Valid()) {
            return false;
        }
        
        byte fhandle = fcb.FileHandle;
        fcb.FileHandle = 0xFF;
        
        return CloseFile(fhandle).IsError == false;
    }
    
    /// <summary>
    /// Finds the first file matching the FCB pattern.
    /// </summary>
    /// <param name="segment">The segment of the FCB.</param>
    /// <param name="offset">The offset of the FCB.</param>
    /// <returns>true if a match was found, false otherwise.</returns>
    public bool FindFirstFcb(ushort segment, ushort offset) {
        DosFileControlBlock fcb = new(_memory, MemoryUtils.ToPhysicalAddress(segment, offset));
        ushort oldDta = TemporaryDiskTransferAreaSegment;
        ushort oldDtaOffset = TemporaryDiskTransferAreaOffset;
        
        // Temporarily change DTA
        SetDiskTransferAreaAddress(oldDta, oldDtaOffset);
        
        string name = _fcbParser.GetFcbName(fcb);
        
        // Get attributes from extended FCB if available
        DosFileAttributes attr = DosFileAttributes.Archive;
        if (fcb.Drive == 0xFF) {
            DosExtendedFileControlBlock extFcb = new(fcb);
            attr = (DosFileAttributes)extFcb.FileAttribute;
        }
        
        // Find first matching file
        bool ret = FindFirstMatchingFile(name, (ushort)attr).IsError == false;
        
        // Restore DTA
        SetDiskTransferAreaAddress(DiskTransferAreaAddressSegment, DiskTransferAreaAddressOffset);
        
        if (ret) {
            // Save result to FCB
            SaveFindResult(fcb);
        }
        
        return ret;
    }
    
    /// <summary>
    /// Finds the next file matching the previous FCB pattern.
    /// </summary>
    /// <param name="segment">The segment of the FCB.</param>
    /// <param name="offset">The offset of the FCB.</param>
    /// <returns>true if a match was found, false otherwise.</returns>
    public bool FindNextFcb(ushort segment, ushort offset) {
        DosFileControlBlock fcb = new(_memory, MemoryUtils.ToPhysicalAddress(segment, offset));
        ushort oldDta = TemporaryDiskTransferAreaSegment;
        ushort oldDtaOffset = TemporaryDiskTransferAreaOffset;
        
        // Temporarily change DTA
        SetDiskTransferAreaAddress(oldDta, oldDtaOffset);
        
        // Find next matching file
        bool ret = FindNextMatchingFile().IsError == false;
        
        // Restore DTA
        SetDiskTransferAreaAddress(DiskTransferAreaAddressSegment, DiskTransferAreaAddressOffset);
        
        if (ret) {
            // Save result to FCB
            SaveFindResult(fcb);
        }
        
        return ret;
    }
    
    /// <summary>
    /// Reads from a file using FCB at the specified memory location.
    /// </summary>
    /// <param name="segment">The segment of the FCB.</param>
    /// <param name="offset">The offset of the FCB.</param>
    /// <param name="recordNumber">The record number to read.</param>
    /// <returns>Status code of the operation.</returns>
    public byte ReadFcb(ushort segment, ushort offset, ushort recordNumber) {
        DosFileControlBlock fcb = new(_memory, MemoryUtils.ToPhysicalAddress(segment, offset));
        byte fhandle;
        ushort recordSize;
        
        fcb.GetSeqData(out fhandle, out recordSize);
        
        // If file is closed but record size is set, reopen it
        if (fhandle == 0xFF && recordSize != 0) {
            if (!OpenFcb(segment, offset)) {
                return FCB_READ_NODATA;
            }
            fcb.GetSeqData(out fhandle, out recordSize);
        }
        
        // Default record size if not set
        if (recordSize == 0) {
            recordSize = 128;
            fcb.SetSeqData(fhandle, recordSize);
        }
        
        // Get current record position
        fcb.GetRecord(out ushort curBlock, out byte curRecord);
        uint pos = ((uint)curBlock * 128 + curRecord) * recordSize;
        
        // Seek to position
        DosFileOperationResult seekResult = MoveFilePointerUsingHandle(SeekOrigin.Begin, fhandle, pos);
        if (seekResult.IsError) {
            return FCB_READ_NODATA;
        }
        
        // Read data
        uint dtaAddress = MemoryUtils.ToPhysicalAddress(DiskTransferAreaAddressSegment, DiskTransferAreaAddressOffset);
        DosFileOperationResult readResult = ReadFile(fhandle, recordSize, dtaAddress + (uint)(recordNumber * recordSize));
        if (readResult.IsError) {
            return FCB_READ_NODATA;
        }
        
        ushort bytesRead = (ushort)readResult.Value!.Value;
        
        // Update record position
        curRecord++;
        if (curRecord > 127) {
            curBlock++;
            curRecord = 0;
        }
        fcb.SetRecord(curBlock, curRecord);
        
        if (bytesRead == 0) {
            return FCB_READ_NODATA;
        }
        
        if (bytesRead < recordSize) {
            // Zero pad the buffer to record size
            for (ushort i = bytesRead; i < recordSize; i++) {
                _memory.UInt8[dtaAddress + (uint)(recordNumber * recordSize + i)] = 0;
            }
            return FCB_READ_PARTIAL;
        }
        
        return FCB_SUCCESS;
    }
    
    /// <summary>
    /// Writes to a file using FCB at the specified memory location.
    /// </summary>
    /// <param name="segment">The segment of the FCB.</param>
    /// <param name="offset">The offset of the FCB.</param>
    /// <param name="recordNumber">The record number to write.</param>
    /// <returns>Status code of the operation.</returns>
    public byte WriteFcb(ushort segment, ushort offset, ushort recordNumber) {
        DosFileControlBlock fcb = new(_memory, MemoryUtils.ToPhysicalAddress(segment, offset));
        byte fhandle;
        ushort recordSize;
        
        fcb.GetSeqData(out fhandle, out recordSize);
        
        // If file is closed but record size is set, reopen it
        if (fhandle == 0xFF && recordSize != 0) {
            if (!OpenFcb(segment, offset)) {
                return FCB_ERR_WRITE;
            }
            fcb.GetSeqData(out fhandle, out recordSize);
        }
        
        // Default record size if not set
        if (recordSize == 0) {
            recordSize = 128;
            fcb.SetSeqData(fhandle, recordSize);
        }
        
        // Get current record position
        fcb.GetRecord(out ushort curBlock, out byte curRecord);
        uint pos = ((uint)curBlock * 128 + curRecord) * recordSize;
        
        // Seek to position
        DosFileOperationResult seekResult = MoveFilePointerUsingHandle(SeekOrigin.Begin, fhandle, pos);
        if (seekResult.IsError) {
            return FCB_ERR_WRITE;
        }
        
        // Write data
        uint dtaAddress = MemoryUtils.ToPhysicalAddress(DiskTransferAreaAddressSegment, DiskTransferAreaAddressOffset);
        DosFileOperationResult writeResult = WriteToFileOrDevice(fhandle, recordSize, dtaAddress + (uint)(recordNumber * recordSize));
        if (writeResult.IsError) {
            return FCB_ERR_WRITE;
        }
        
        ushort bytesWritten = (ushort)writeResult.Value!.Value;
        
        // Update size, date, and time
        fcb.GetSizeDateTime(out uint size, out ushort date, out ushort time);
        if (pos + bytesWritten > size) {
            size = pos + bytesWritten;
        }
        
        // Update current date and time
        date = PackDate(DateTime.Now);
        time = PackTime(DateTime.Now);
        
        // If we have an open file, update its date and time
        if (fhandle < OpenFiles.Length && OpenFiles[fhandle] is DosFile dosFile) {
            dosFile.Date = date;
            dosFile.Time = time;
        }
        
        fcb.SetSizeDateTime(size, date, time);
        
        // Update record position
        curRecord++;
        if (curRecord > 127) {
            curBlock++;
            curRecord = 0;
        }
        fcb.SetRecord(curBlock, curRecord);
        
        return FCB_SUCCESS;
    }
    
    /// <summary>
    /// Increases the size of a file using FCB at the specified memory location.
    /// </summary>
    /// <param name="segment">The segment of the FCB.</param>
    /// <param name="offset">The offset of the FCB.</param>
    /// <returns>Status code of the operation.</returns>
    public byte IncreaseFcbFileSize(ushort segment, ushort offset) {
        DosFileControlBlock fcb = new(_memory, MemoryUtils.ToPhysicalAddress(segment, offset));
        byte fhandle = fcb.FileHandle;
        
        // Get current record position
        fcb.GetRecord(out ushort curBlock, out byte curRecord);
        ushort recordSize = fcb.LogicalRecordSize;
        uint pos = ((uint)curBlock * 128 + curRecord) * recordSize;
        
        // Seek to position
        DosFileOperationResult seekResult = MoveFilePointerUsingHandle(SeekOrigin.Begin, fhandle, pos);
        if (seekResult.IsError) {
            return FCB_ERR_WRITE;
        }
        
        // Write 0 bytes to update file size
        DosFileOperationResult writeResult = WriteToFileOrDevice(fhandle, 0, 0);
        if (writeResult.IsError) {
            return FCB_ERR_WRITE;
        }
        
        // Update size, date, and time
        fcb.GetSizeDateTime(out uint size, out _, out _);
        if (pos > size) {
            size = pos;
        }
        
        // Update current date and time
        ushort date = PackDate(DateTime.Now);
        ushort time = PackTime(DateTime.Now);
        
        // If we have an open file, update its date and time
        if (fhandle < OpenFiles.Length && OpenFiles[fhandle] is DosFile dosFile) {
            dosFile.Date = date;
            dosFile.Time = time;
        }
        
        fcb.SetSizeDateTime(size, date, time);
        return FCB_SUCCESS;
    }
    
    /// <summary>
    /// Deletes a file using FCB at the specified memory location.
    /// </summary>
    /// <param name="segment">The segment of the FCB.</param>
    /// <param name="offset">The offset of the FCB.</param>
    /// <returns>true if successful, false otherwise.</returns>
    public bool DeleteFileFcb(ushort segment, ushort offset) {
        DosFileControlBlock fcb = new(_memory, MemoryUtils.ToPhysicalAddress(segment, offset));
        ushort oldDta = TemporaryDeleteFcbSegment;
        ushort oldDtaOffset = TemporaryDeleteFcbOffset;
        uint newDta = MemoryUtils.ToPhysicalAddress(oldDta, oldDtaOffset);
        
        // Temporarily change DTA
        SetDiskTransferAreaAddress(oldDta, oldDtaOffset);
        
        // FCB DELETE supports wildcards - will return true if one or more files are deleted
        bool returnValue = false;
        bool nextFile = FindFirstFcb(segment, offset);
        
        // Create a new FCB at the temporary DTA location
        DosFileControlBlock tempFcb = new(_memory, newDta);
        
        while (nextFile) {
            string shortName = _fcbParser.GetFcbName(tempFcb);
            bool res = RemoveFile(shortName).IsError == false;
            
            // Set return value to true if at least one file was deleted
            if (!returnValue && res) {
                returnValue = true;
            }
            
            nextFile = FindNextFcb(segment, offset);
        }
        
        // Restore DTA
        SetDiskTransferAreaAddress(DiskTransferAreaAddressSegment, DiskTransferAreaAddressOffset);
        
        return returnValue;
    }
    
    /// <summary>
    /// Renames a file using FCB at the specified memory location.
    /// </summary>
    /// <param name="segment">The segment of the FCB.</param>
    /// <param name="offset">The offset of the FCB.</param>
    /// <returns>true if successful, false otherwise.</returns>
    public bool RenameFileFcb(ushort segment, ushort offset) {
        DosFileControlBlock oldFcb = new(_memory, MemoryUtils.ToPhysicalAddress(segment, offset));
        DosFileControlBlock newFcb = new(_memory, MemoryUtils.ToPhysicalAddress(segment, (ushort)(offset + 16)));
        
        if (!oldFcb.Valid()) {
            return false;
        }
        
        string oldName = _fcbParser.GetFcbName(oldFcb);
        string newName = _fcbParser.GetFcbName(newFcb);
        
        // Check if the source file is still open
        string? fullOldName = TryGetFullHostPathFromDos(oldName);
        if (string.IsNullOrEmpty(fullOldName)) {
            return false;
        }
        
        // Close the file if it's open
        for (ushort i = 0; i < OpenFiles.Length; i++) {
            if (OpenFiles[i] is DosFile dosFile && dosFile.Name.Equals(fullOldName, StringComparison.OrdinalIgnoreCase)) {
                CloseFile(i);
            }
        }
        
        // Rename the file
        return _dosPathResolver.RenameFile(oldName, newName).IsError == false;
    }
    
    /// <summary>
    /// Gets the file size for FCB at the specified memory location.
    /// </summary>
    /// <param name="segment">The segment of the FCB.</param>
    /// <param name="offset">The offset of the FCB.</param>
    /// <returns>true if successful, false otherwise.</returns>
    public bool GetFileSizeFcb(ushort segment, ushort offset) {
        DosFileControlBlock fcb = new(_memory, MemoryUtils.ToPhysicalAddress(segment, offset));
        string shortname = _fcbParser.GetFcbName(fcb);
        
        // Open the file in read mode
        ushort entry = fcb.FileHandle;
        if (OpenFile(shortname, FileAccessMode.ReadOnly).IsError) {
            return false;
        }
        
        // Get file size
        uint size = 0;
        MoveFilePointerUsingHandle(SeekOrigin.End, entry, 0);
        if (MoveFilePointerUsingHandle(SeekOrigin.Current, entry, 0).IsError) {
            CloseFile(entry);
            return false;
        }
        
        // Close file
        CloseFile(entry);
        
        // Calculate random record
        ushort recSize = fcb.LogicalRecordSize;
        if (recSize == 0) {
            recSize = 128;
        }
        
        uint random = (size / recSize);
        if (size % recSize != 0) {
            random++;
        }
        
        fcb.SetRandom(random);
        return true;
    }
    
    /// <summary>
    /// Performs a random read operation using FCB.
    /// </summary>
    /// <param name="segment">The segment of the FCB.</param>
    /// <param name="offset">The offset of the FCB.</param>
    /// <param name="numRec">Number of records to read.</param>
    /// <param name="restore">Whether to restore record position after read.</param>
    /// <returns>Status code of the operation.</returns>
    public byte RandomReadFcb(ushort segment, ushort offset, ref ushort numRec, bool restore) {
        DosFileControlBlock fcb = new(_memory, MemoryUtils.ToPhysicalAddress(segment, offset));
        
        // Get random record and set current record
        uint random = fcb.GetRandom();
        fcb.SetRecord((ushort)(random / 128), (byte)(random & 127));
        
        // Store old position if restore is requested
        ushort oldBlock = 0;
        byte oldRec = 0;
        if (restore) {
            fcb.GetRecord(out oldBlock, out oldRec);
        }
        
        // Read records
        byte error = FCB_SUCCESS;
        ushort count;
        for (count = 0; count < numRec; count++) {
            error = ReadFcb(segment, offset, count);
            if (error != FCB_SUCCESS) {
                break;
            }
        }
        
        if (error == FCB_READ_PARTIAL) {
            count++; // partial read counts
        }
        
        numRec = count;
        
        // Get new position
        fcb.GetRecord(out ushort newBlock, out byte newRec);
        if (restore) {
            fcb.SetRecord(oldBlock, oldRec);
        } else {
            // Update random record pointer with new position
            fcb.SetRandom((uint)(newBlock * 128 + newRec));
        }
        
        return error;
    }
    
    /// <summary>
    /// Performs a random write operation using FCB.
    /// </summary>
    /// <param name="segment">The segment of the FCB.</param>
    /// <param name="offset">The offset of the FCB.</param>
    /// <param name="numRec">Number of records to write.</param>
    /// <param name="restore">Whether to restore record position after write.</param>
    /// <returns>Status code of the operation.</returns>
    public byte RandomWriteFcb(ushort segment, ushort offset, ref ushort numRec, bool restore) {
        DosFileControlBlock fcb = new(_memory, MemoryUtils.ToPhysicalAddress(segment, offset));
        
        // Get random record and set current record
        uint random = fcb.GetRandom();
        fcb.SetRecord((ushort)(random / 128), (byte)(random & 127));
        
        // Store old position if restore is requested
        ushort oldBlock = 0;
        byte oldRec = 0;
        if (restore) {
            fcb.GetRecord(out oldBlock, out oldRec);
        }
        
        byte error = FCB_SUCCESS;
        
        if (numRec > 0) {
            // Write records
            ushort count;
            for (count = 0; count < numRec; count++) {
                error = WriteFcb(segment, offset, count);
                if (error != FCB_SUCCESS) {
                    break;
                }
            }
            numRec = count;
        } else {
            // Increase file size
            IncreaseFcbFileSize(segment, offset);
        }
        
        // Get new position
        fcb.GetRecord(out ushort newBlock, out byte newRec);
        if (restore) {
            fcb.SetRecord(oldBlock, oldRec);
        } else {
            // Update random record pointer with new position
            fcb.SetRandom((uint)(newBlock * 128 + newRec));
        }
        
        return error;
    }
    
    /// <summary>
    /// Sets the random record number for FCB based on current record position.
    /// </summary>
    /// <param name="segment">The segment of the FCB.</param>
    /// <param name="offset">The offset of the FCB.</param>
    public void SetRandomRecordFcb(ushort segment, ushort offset) {
        DosFileControlBlock fcb = new(_memory, MemoryUtils.ToPhysicalAddress(segment, offset));
        fcb.GetRecord(out ushort block, out byte rec);
        fcb.SetRandom((uint)(block * 128 + rec));
    }
    
    /// <summary>
    /// Parses a filename into an FCB.
    /// </summary>
    /// <param name="segment">Segment of the FCB.</param>
    /// <param name="offset">Offset of the FCB.</param>
    /// <param name="parser">Parsing flags.</param>
    /// <param name="filename">Filename to parse.</param>
    /// <param name="changeOffset">Out parameter that will contain how many bytes were processed.</param>
    /// <returns>A parse result indicating success or failure.</returns>
    public FcbParseResult ParseFcbName(ushort segment, ushort offset, byte parser, string filename, out byte changeOffset) {
        return _fcbParser.ParseName(segment, offset, parser, filename, out changeOffset);
    }
    
    /// <summary>
    /// Sets up a file for use with FCB.
    /// </summary>
    /// <param name="fcb">The FCB to set up.</param>
    /// <param name="handle">The file handle.</param>
    private void FileOpenFcb(DosFileControlBlock fcb, byte handle) {
        fcb.Drive = (byte)(fcb.Drive == 0 ? _dosDriveManager.CurrentDriveIndex + 1 : fcb.Drive);
        fcb.FileHandle = handle;
        fcb.CurrentBlock = 0;
        fcb.LogicalRecordSize = 128;
        
        // Get file size
        uint size = 0;
        if (OpenFiles[handle] is DosFile file) {
            size = (uint)file.Length;
            fcb.SetSizeDateTime(size, file.Date, file.Time);
        }
        
        // Seek to beginning of file
        MoveFilePointerUsingHandle(SeekOrigin.Begin, handle, 0);
    }
    
    /// <summary>
    /// Saves a find result to the FCB.
    /// </summary>
    /// <param name="fcb">The FCB to save the result to.</param>
    private void SaveFindResult(DosFileControlBlock fcb) {
        DosDiskTransferArea findDta = new(_memory, MemoryUtils.ToPhysicalAddress(TemporaryDiskTransferAreaSegment, TemporaryDiskTransferAreaOffset));
        DosSearchResult searchResult = findDta.GetSearchResult();
        byte drive = (byte)(fcb.Drive + 1);
        
        // Get attributes
        DosFileAttributes findAttr = DosFileAttributes.Normal;
        if (fcb.Drive == 0xFF) {
            // Gets search attributes from extended FCB
            DosExtendedFileControlBlock extFcb = new(fcb);
            findAttr = (DosFileAttributes)extFcb.FileAttribute;
        }
        
        // Split the filename and extension
        var (fileName, ext) = DosFileControlBlockParser.SplitFcbName(searchResult.Name);
        
        // Create a new FCB at the DTA location
        DosFileControlBlock tempFcb = new(_memory, MemoryUtils.ToPhysicalAddress(DiskTransferAreaAddressSegment, DiskTransferAreaAddressOffset));
        tempFcb.Create(fcb.Drive == 0xFF);
        
        // Set file information
        tempFcb.SetName(drive, fileName, ext);
        
        // Set attributes if the FCB is extended
        if (fcb.Drive == 0xFF) {
            DosExtendedFileControlBlock extTempFcb = new(tempFcb);
            extTempFcb.FileAttribute = (byte)findAttr;
        }
        
        // Set result data
        tempFcb.SetResult(searchResult.Size, searchResult.Date, searchResult.Time, searchResult.Attributes);
    }
    
    /// <summary>
    /// Checks if the FCB is valid.
    /// </summary>
    /// <param name="fcb">The FCB to check.</param>
    /// <returns>true if valid, false otherwise.</returns>
    private bool IsFcbValid(DosFileControlBlock fcb) {
        // Simple check for filename or file handle (from DOSBox for Oubliette game)
        return fcb.FileName[0] != 0 || fcb.FileHandle != 0xFF;
    }

    /// <summary>
    /// Packs a DateTime into a DOS date value.
    /// </summary>
    /// <param name="dateTime">The DateTime to pack.</param>
    /// <returns>A DOS format date value.</returns>
    private static ushort PackDate(DateTime dateTime) {
        int year = dateTime.Year - 1980;
        int month = dateTime.Month;
        int day = dateTime.Day;
        
        return (ushort)((year << 9) | (month << 5) | day);
    }
    
    /// <summary>
    /// Packs a DateTime into a DOS time value.
    /// </summary>
    /// <param name="dateTime">The DateTime to pack.</param>
    /// <returns>A DOS format time value.</returns>
    private static ushort PackTime(DateTime dateTime) {
        int hour = dateTime.Hour;
        int minute = dateTime.Minute;
        int second = dateTime.Second / 2; // DOS stores seconds divided by 2
        
        return (ushort)((hour << 11) | (minute << 5) | second);
    }
}