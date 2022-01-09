namespace Spice86.Emulator.InterruptHandlers.Dos;

using Serilog;

using Spice86.Emulator.Errors;
using Spice86.Emulator.Memory;
using Spice86.Utils;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

public class DosFileManager
{
    private static readonly ILogger _logger = Log.Logger.ForContext<DosFileManager>();
    public const int FileHandleOffset = 5;
    private const int MaxOpenFiles = 15;
    private static readonly Dictionary<int, string> _fileOpenMode = new();
    static DosFileManager()
    {
        _fileOpenMode.Add(0x00, "r");
        _fileOpenMode.Add(0x01, "w");
        _fileOpenMode.Add(0x02, "rw");
    }

    private Memory memory;
    private OpenFile[] openFiles = new OpenFile[MaxOpenFiles];
    private string? currentDir;
    private Dictionary<char, string> driveMap = new();
    private int diskTransferAreaAddressSegment;
    private int diskTransferAreaAddressOffset;
    private string? currentMatchingFileSearchFolder;
    private string? currentMatchingFileSearchSpec;
    private IEnumerator<string>? matchingFilesIterator;
    public DosFileManager(Memory memory)
    {
        this.memory = memory;
    }

    public virtual void SetDiskTransferAreaAddress(int diskTransferAreaAddressSegment, int diskTransferAreaAddressOffset)
    {
        this.diskTransferAreaAddressSegment = diskTransferAreaAddressSegment;
        this.diskTransferAreaAddressOffset = diskTransferAreaAddressOffset;
    }

    public virtual int GetDiskTransferAreaAddressSegment()
    {
        return diskTransferAreaAddressSegment;
    }

    public virtual int GetDiskTransferAreaAddressOffset()
    {
        return diskTransferAreaAddressOffset;
    }

    private int GetDiskTransferAreaAddressPhysical()
    {
        return MemoryUtils.ToPhysicalAddress(diskTransferAreaAddressSegment, diskTransferAreaAddressOffset);
    }

    public virtual void SetDiskParameters(string currentDir, Dictionary<char, string> driveMap)
    {
        this.currentDir = currentDir;
        this.driveMap = driveMap;
    }

    public virtual DosFileOperationResult SetCurrentDir(string currentDir)
    {
        this.currentDir = ToHostCaseSensitiveFileName(currentDir, false);
        return DosFileOperationResult.NoValue();
    }

    /// <summary>
    /// Converts dosFileName to a host file name.<br/>
    /// For this, need to:
    /// <ul>
    /// <li>Prefix either the current folder or the drive folder.</li>
    /// <li>Replace backslashes with slashes</li>
    /// <li>Find case sensitive matches for every path item (since DOS is case insensitive but some OS are not)</li>
    /// </ul>
    /// </summary>
    /// <param name="dosFileName"></param>
    /// <param name="caseSensitiveOnlyParent">
    ///          if true will try to find case sensitive match for only the parent of the file (useful when creating a
    ///          file)</param>
    /// <returns>the file name in the host file system, or null if nothing was found.</returns>
    private string? ToHostCaseSensitiveFileName(string dosFileName, bool caseSensitiveOnlyParent)
    {
        string fileName = ToHostFileName(dosFileName);
        if (caseSensitiveOnlyParent)
        {
            var file = new FileInfo(fileName);
            string? parent = ToCaseSensitiveFileName(Directory.GetParent(file.FullName)?.FullName);
            if (parent == null)
            {
                return null;
            }

            var combinedPath = Path.Combine(parent, file.Name);
            return combinedPath;
        }
        else
        {
            return ToCaseSensitiveFileName(fileName);
        }
    }

    public virtual DosFileOperationResult CreateFileUsingHandle(string fileName, int fileAttribute)
    {
        string? hostFileName = ToHostCaseSensitiveFileName(fileName, true);
        if (hostFileName == null)
        {
            return FileNotFoundError(fileName, "Could not find parent of {} so cannot create file.");
        }

        _logger.Information("Creating file {@HostFileName} with attribute {@FileAttribute}", hostFileName, fileAttribute);
        var path = new FileInfo(hostFileName);
        try
        {
            if (File.Exists(path.FullName))
            {
                File.Delete(path.FullName);
            }

            File.Create(path.FullName);
        }
        catch (IOException e)
        {
            throw new UnrecoverableException("IOException while creating file", e);
        }

        return OpenFileInternal(fileName, hostFileName, "rw");
    }

    public virtual DosFileOperationResult OpenFile(string fileName, int rwAccessMode)
    {
        string hostFileName = ToHostCaseSensitiveFileName(fileName, false);
        if (hostFileName == null)
        {
            return this.FileNotFoundError(fileName);
        }

        string openMode = _fileOpenMode[rwAccessMode];
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information))
        {
            _logger.Information("Opening file {@HostFileName} with mode {@OpenMode}", hostFileName, openMode);
        }

        return OpenFileInternal(fileName, hostFileName, openMode);
    }

    public virtual DosFileOperationResult DuplicateFileHandle(int fileHandle)
    {
        OpenFile file = GetOpenFile(fileHandle);
        if (file == null)
        {
            return FileNotOpenedError(fileHandle);
        }

        int? freeIndex = FindNextFreeFileIndex();
        if (freeIndex == null)
        {
            return NoFreeHandleError();
        }

        int dosIndex = freeIndex.Value + FileHandleOffset;
        SetOpenFile(dosIndex, file);
        return DosFileOperationResult.Value16(dosIndex);
    }

    public virtual DosFileOperationResult CloseFile(int fileHandle)
    {
        OpenFile file = GetOpenFile(fileHandle);
        if (file == null)
        {
            return FileNotOpenedError(fileHandle);
        }

        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information))
        {
            _logger.Information("Closed {@ClosedFileName}, file was loaded in ram in those addresses: {@ClosedFileAddresses}", file.GetName(), file.GetLoadMemoryRanges());
        }

        SetOpenFile(fileHandle, null);
        try
        {
            if (CountHandles(file) == 0)
            {

                // Only close the file if no other handle to it exist.
                file.GetRandomAccessFile().Close();
            }
        }
        catch (IOException e)
        {
            throw new UnrecoverableException("IOException while closing file", e);
        }

        return DosFileOperationResult.NoValue();
    }

    public virtual DosFileOperationResult ReadFile(int fileHandle, int readLength, int targetAddress)
    {
        OpenFile file = GetOpenFile(fileHandle);
        if (file == null)
        {
            return FileNotOpenedError(fileHandle);
        }

        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information))
        {
            _logger.Information("Reading from file {@FileName}", file.GetName());
        }

        byte[] buffer = new byte[readLength];
        int actualReadLength;
        try
        {
            actualReadLength = file.GetRandomAccessFile().Read(buffer, 0, readLength);
        }
        catch (IOException e)
        {
            throw new UnrecoverableException("IOException while reading file", e);
        }

        if (actualReadLength == -1)
        {

            // EOF
            return DosFileOperationResult.Value16(0);
        }

        if (actualReadLength > 0)
        {
            memory.LoadData(targetAddress, buffer, actualReadLength);
            file.AddMemoryRange(new MemoryRange(targetAddress, targetAddress + actualReadLength - 1, file.GetName()));
        }

        return DosFileOperationResult.Value16(actualReadLength);
    }

    private DosFileOperationResult WriteToDevice(int fileHandle, int writeLength, int bufferAddress)
    {
        string deviceName = GetDeviceName(fileHandle);
        byte[] buffer = memory.GetData(bufferAddress, writeLength);
        System.Console.WriteLine(deviceName + ConvertUtils.ToString(buffer));
        return DosFileOperationResult.Value16(writeLength);
    }

    public string GetDeviceName(int fileHandle)
    {
        return fileHandle switch
        {
            0 => "STDIN",
            1 => "STDOUT",
            2 => "STDERR",
            3 => "STDAUX",
            4 => "STDPRN",
            _ => throw new UnrecoverableException(
                "This is a programming error. getDeviceName called with fileHandle=" + fileHandle)
        };
    }

    public virtual DosFileOperationResult WriteFileUsingHandle(int fileHandle, int writeLength, int bufferAddress)
    {
        if (IsWriteDeviceFileHandle(fileHandle))
        {
            return WriteToDevice(fileHandle, writeLength, bufferAddress);
        }

        if (!IsValidFileHandle(fileHandle))
        {
            _logger.Warning("Invalid or unsupported file handle {@FileHandle}. Doing nothing.", fileHandle);

            // Fake that we wrote, this could be used to write to stdout / stderr ...
            return DosFileOperationResult.Value16(writeLength);
        }

        OpenFile file = GetOpenFile(fileHandle);
        if (file == null)
        {
            return FileNotOpenedError(fileHandle);
        }

        try
        {
            file.GetRandomAccessFile().Write(memory.GetRam(), bufferAddress, writeLength);
        }
        catch (IOException e)
        {
            throw new UnrecoverableException("IOException while writing file", e);
        }

        return DosFileOperationResult.Value16(writeLength);
    }

    public virtual DosFileOperationResult MoveFilePointerUsingHandle(int originOfMove, int fileHandle, int offset)
    {
        OpenFile file = GetOpenFile(fileHandle);
        if (file == null)
        {
            return FileNotOpenedError(fileHandle);
        }

        _logger.Information("Moving in file {@FileMove}", file.GetName());
        FileStream randomAccessFile = file.GetRandomAccessFile();
        try
        {
            int newOffset = Seek(randomAccessFile, originOfMove, offset);
            return DosFileOperationResult.Value32(newOffset);
        }
        catch (IOException e)
        {
            _logger.Error(e, "An error occurred while seeking file {@Error}", e);
            return DosFileOperationResult.Error(0x19);
        }
    }

    /// <summary>
    /// </summary>
    /// <param name="fileSpec">a filename with ? when any character can match or * when multiple characters can match. Case is insensitive</param>
    /// <returns></returns>
    public virtual DosFileOperationResult FindFirstMatchingFile(string fileSpec)
    {
        string hostSearchSpec = ToHostFileName(fileSpec);
        currentMatchingFileSearchFolder = hostSearchSpec.Substring(0, hostSearchSpec.LastIndexOf('/') + 1);
        currentMatchingFileSearchSpec = hostSearchSpec.Replace(currentMatchingFileSearchFolder, "");
        Regex currentMatchingFileSearchSpecPattern = FileSpecToRegex(currentMatchingFileSearchSpec);
        try
        {
            var pathes = Directory.GetFiles(currentMatchingFileSearchFolder);
            List<string> matchingPathes = pathes.Where((p) => MatchesSpec(currentMatchingFileSearchSpecPattern, new FileInfo(p))).ToList();
            matchingFilesIterator = matchingPathes.GetEnumerator();
            return FindNextMatchingFile();
        }
        catch (IOException e)
        {
            _logger.Error(e, "Error while walking path {@CurrentMatchingFileSearchFolder} or getting attributes.", currentMatchingFileSearchFolder);
            return DosFileOperationResult.Error(0x03);
        }
    }

    /// <summary>
    /// Converts a dos filespec to a regex pattern
    /// </summary>
    /// <param name="fileSpec"></param>
    /// <returns></returns>
    private Regex FileSpecToRegex(string fileSpec)
    {
        string regex = fileSpec.ToLowerInvariant();
        regex = regex.Replace("\\.", "\\\\.");
        regex = regex.Replace("\\?", ".");
        regex = regex.Replace("\\*", ".*");
        return new Regex(regex);
    }

    public virtual DosFileOperationResult FindNextMatchingFile()
    {
        if (matchingFilesIterator == null)
        {
            _logger.Warning("No search was done");
            return FileNotFoundError(null);
        }

        if (!matchingFilesIterator.MoveNext())
        {
            _logger.Warning("No more files matching {@CurrentMatchingFileSearchSpec} in path {@CurrentMatchingFileSearchFolder}", currentMatchingFileSearchSpec, currentMatchingFileSearchFolder);
            return FileNotFoundError(null);
        }

        var matching = matchingFilesIterator.MoveNext();
        if(matching)
        {
            try
            {
                UpdateDTAFromFile(matchingFilesIterator.Current);
            }
            catch (IOException e)
            {
                _logger.Warning("Error while getting attributes.");
                return FileNotFoundError(null);
            }
        }

        return DosFileOperationResult.NoValue();
    }

    /// <summary>
    /// </summary>
    /// <param name="fileSpecPattern">
    ///          a regex to match for a file, lower case</param>
    /// <param name="item">
    ///          a path from which the file to match will be extracted</param>
    /// <returns>true if it matched, false otherwise</returns>
    private bool MatchesSpec(Regex fileSpecPattern, FileInfo item)
    {
        if (Directory.Exists(item.DirectoryName))
        {
            return false;
        }

        string fileName = item.FullName.ToLowerInvariant();
        return fileSpecPattern.IsMatch(fileName);
    }

    private void UpdateDTAFromFile(string matchingFile)
    {
        _logger.Information("Found matching file {@MatchingFile}", matchingFile);
        DosDiskTransferArea dosDiskTransferArea = new(this.memory, this.GetDiskTransferAreaAddressPhysical());
        var attributes = new FileInfo(matchingFile);
        DateTime creationZonedDateTime = attributes.CreationTimeUtc;
        DateTime creationLocalDate = creationZonedDateTime.ToLocalTime();
        DateTime creationLocalTime = creationZonedDateTime.ToLocalTime();
        dosDiskTransferArea.SetFileDate(ToDosDate(creationLocalDate));
        dosDiskTransferArea.SetFileTime(ToDosTime(creationLocalTime));
        dosDiskTransferArea.SetFileSize((int)attributes.Length);
        dosDiskTransferArea.SetFileName(matchingFile);
    }

    private int ToDosDate(DateTime localDate)
    {
        // https://stanislavs.org/helppc/file_attributes.html
        int day = localDate.Day;
        int month = localDate.Month;
        int dosYear = localDate.Year - 1980;
        return (day & 0b11111) | ((month & 0b1111) << 5) | ((dosYear & 0b1111111) << 9);
    }

    private int ToDosTime(DateTime localTime)
    {
        // https://stanislavs.org/helppc/file_attributes.html
        int dosSeconds = localTime.Second / 2;
        int minutes = localTime.Minute;
        int hours = localTime.Hour;
        return (dosSeconds & 0b11111) | ((minutes & 0b111111) << 5) | ((hours & 0b11111) << 11);
    }

    private int Seek(FileStream randomAccessFile, int originOfMove, int offset)
    {
        long newOffset;
        if (originOfMove == 0)
        {
            newOffset = offset; // seek from beginning, offset is good
        }
        else if (originOfMove == 1)
        {

            // seek from last read
            newOffset = randomAccessFile.Position + offset;
        }
        else
        {

            // seek from end
            newOffset = randomAccessFile.Length - offset;
        }

        randomAccessFile.Seek(newOffset, SeekOrigin.Begin);
        return (int)newOffset;
    }

    private DosFileOperationResult FileNotFoundError(string fileName)
    {
        return FileNotFoundError(fileName, "File {} not found!");
    }

    private DosFileOperationResult FileNotFoundError(string fileName, string message)
    {
        if (fileName != null)
        {
            _logger.Warning("File not foud in {@MethodName} {@Message} {@FileName}", nameof(FileNotFoundError), message, fileName);
        }

        return DosFileOperationResult.Error(0x02);
    }

    private DosFileOperationResult NoFreeHandleError()
    {
        _logger.Warning("Could not find a free handle {@MethodName}", nameof(NoFreeHandleError));
        return DosFileOperationResult.Error(0x04);
    }

    private DosFileOperationResult FileNotOpenedError(int fileHandle)
    {
        _logger.Warning("File not opened: {@FileHandle}", fileHandle);
        return DosFileOperationResult.Error(0x06);
    }

    private int CountHandles(OpenFile openFileToCount)
    {
        int count = 0;
        foreach (OpenFile openFile in openFiles)
        {
            if (openFile == openFileToCount)
            {
                count++;
            }
        }

        return count;
    }

    private OpenFile GetOpenFile(int fileHandle)
    {
        return openFiles[FileHandleToIndex(fileHandle)];
    }

    private void SetOpenFile(int fileHandle, OpenFile openFile)
    {
        openFiles[FileHandleToIndex(fileHandle)] = openFile;
    }

    private DosFileOperationResult OpenFileInternal(string fileName, string hostFileName, string openMode)
    {
        if (hostFileName == null)
        {

            // Not found
            return FileNotFoundError(fileName);
        }

        int? freeIndex = FindNextFreeFileIndex();
        if (freeIndex == null)
        {
            return NoFreeHandleError();
        }

        int dosIndex = freeIndex.Value + FileHandleOffset;
        try
        {
            FileAccess fileAccess = FileAccess.Read;
            if(openMode == "w")
            {
                fileAccess = FileAccess.Write;
            }
            if(openMode == "rw")
            {
                fileAccess = FileAccess.ReadWrite;
            }
            FileStream? randomAccessFile = null;
            if(fileAccess == FileAccess.Read)
            {
                randomAccessFile = File.OpenRead(hostFileName);
            }
            if (fileAccess == FileAccess.Write)
            {
                randomAccessFile = File.OpenWrite(hostFileName);
            }
            if (fileAccess == FileAccess.ReadWrite)
            {
                randomAccessFile = File.Open(hostFileName, FileMode.Open);
            }
            if (randomAccessFile != null)
            {
                SetOpenFile(dosIndex, new OpenFile(fileName, dosIndex, randomAccessFile));
            }
        }
        catch (FileNotFoundException)
        {
            return FileNotFoundError(fileName);
        }

        return DosFileOperationResult.Value16(dosIndex);
    }

    private bool IsWriteDeviceFileHandle(int fileHandle)
    {
        return fileHandle > 0 && fileHandle < FileHandleOffset;
    }

    private bool IsValidFileHandle(int fileHandle)
    {
        return fileHandle >= FileHandleOffset && fileHandle <= MaxOpenFiles + FileHandleOffset;
    }

    private int FileHandleToIndex(int fileHandle)
    {
        return fileHandle - FileHandleOffset;
    }

    private int? FindNextFreeFileIndex()
    {
        for (int i = 0; i < openFiles.Length; i++)
        {
            if (openFiles[i] == null)
            {
                return i;
            }
        }

        return null;
    }
    private string ReplaceDriveWithHostPath(string fileName)
    {

        // Absolute path
        char driveLetter = fileName[0];
        
        if (driveMap.TryGetValue(driveLetter, out var pathForDrive))
        {
            throw new UnrecoverableException("Could not find a mapping for drive " + driveLetter);
        }

        return fileName.Replace(driveLetter + ":", pathForDrive);
    }

    /// <summary>
    /// Prefixes the given filename by either the mapped drive folder or the current folder depending on whether there is
    /// a Drive in the filename or not.<br/>
    /// Does not convert to case sensitive filename.
    /// </summary>
    /// <param name="dosFileName"></param>
    /// <returns></returns>
    private string ToHostFileName(string dosFileName)
    {
        string fileName = dosFileName.Replace('\\', '/');
        if (fileName.Length >= 2 && fileName[1] == ':')
        {
            fileName = ReplaceDriveWithHostPath(fileName);
        }
        else
        {
            fileName = currentDir + fileName;
        }

        return fileName.Replace("//", "/");
    }

    private string? ToCaseSensitiveFileName(string? caseInsensitivePath)
    {
        if (string.IsNullOrWhiteSpace(caseInsensitivePath))
        {
            return null;
        }

        FileInfo fileToProcess = new FileInfo(caseInsensitivePath);
        if (File.Exists(fileToProcess.FullName) == false ||
            Path.GetPathRoot(fileToProcess.FullName) == Directory.GetParent(fileToProcess.FullName)?.FullName)
        {
            // file exists or root reached, no need to go further
            return caseInsensitivePath;
        }

        string? parent = ToCaseSensitiveFileName(Directory.GetParent(fileToProcess.FullName)?.FullName);
        if (parent == null)
        {
            return null;
        }

        try
        {
            string? filename = Directory.GetFiles(parent)
                .Where(x => FileSpecToRegex(fileToProcess.Name).IsMatch(x))
                .FirstOrDefault();
            return filename;
        }
        catch (IOException e)
        {
            _logger.Warning(e, "Error while checking file {@CaseInsensitivePath}: {@Exception}", caseInsensitivePath, e);
        }

        return null;
    }
}
