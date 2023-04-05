using Spice86.Logging;
using Spice86.Shared.Interfaces;

namespace Spice86.Core.Emulator.OperatingSystem;

using Serilog;

using Spice86.Core.Emulator.Memory;
using Spice86.Core.Utils;

using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.OperatingSystem.Devices;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Diagnostics;

public class DosFileManager {
    private const int MaxOpenFiles = 20;
    private static readonly Dictionary<byte, string> _fileOpenMode = new();
    private readonly ILoggerService _loggerService;
    private string? _currentDir;

    private string? _currentMatchingFileSearchFolder;

    private string? _currentMatchingFileSearchSpec;

    private ushort _diskTransferAreaAddressOffset;

    private ushort _diskTransferAreaAddressSegment;

    private Dictionary<char, string> _driveMap = new();

    private IEnumerator<string>? _matchingFilesIterator;

    private readonly Memory _memory;

    private readonly OpenFile?[] _openFiles = new OpenFile[MaxOpenFiles];
    
    private readonly Dos _dos;

    static DosFileManager() {
        _fileOpenMode.Add(0x00, "r");
        _fileOpenMode.Add(0x01, "w");
        _fileOpenMode.Add(0x02, "rw");
    }

    public DosFileManager(Memory memory, ILoggerService loggerService, Dos dos) {
        _loggerService = loggerService;
        _memory = memory;
        _dos = dos;
    }

    public DosFileOperationResult CloseFile(ushort fileHandle) {
        OpenFile? file = GetOpenFile(fileHandle);
        if (file == null) {
            return FileNotOpenedError(fileHandle);
        }

        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _loggerService.Information("Closed {@ClosedFileName}, file was loaded in ram in those addresses: {@ClosedFileAddresses}", file.Name, file.LoadedMemoryRanges);
        }

        SetOpenFile(fileHandle, null);
        try {
            if (CountHandles(file) == 0) {
                // Only close the file if no other handle to it exist.
                file.RandomAccessFile.Close();
            }
        } catch (IOException e) {
            e.Demystify();
            throw new UnrecoverableException("IOException while closing file", e);
        }

        return DosFileOperationResult.NoValue();
    }

    public DosFileOperationResult CreateFileUsingHandle(string fileName, ushort fileAttribute) {
        string? hostFileName = ToHostCaseSensitiveFileName(fileName, true);
        if (hostFileName == null) {
            return FileNotFoundErrorWithLog($"Could not find parent of {fileName} so cannot create file.");
        }

        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _loggerService.Information("Creating file {@HostFileName} with attribute {@FileAttribute}", hostFileName, fileAttribute);
        }
        FileInfo path = new FileInfo(hostFileName);
        try {
            if (File.Exists(path.FullName)) {
                File.Delete(path.FullName);
            }

            File.Create(path.FullName).Close();
        } catch (IOException e) {
            e.Demystify();
            throw new UnrecoverableException("IOException while creating file", e);
        }

        return OpenFileInternal(fileName, hostFileName, "rw");
    }

    public DosFileOperationResult DuplicateFileHandle(ushort fileHandle) {
        OpenFile? file = GetOpenFile(fileHandle);
        if (file == null) {
            return FileNotOpenedError(fileHandle);
        }

        int? freeIndex = FindNextFreeFileIndex();
        if (freeIndex == null) {
            return NoFreeHandleError();
        }

        ushort dosIndex = (ushort)freeIndex.Value;
        SetOpenFile(dosIndex, file);
        return DosFileOperationResult.Value16(dosIndex);
    }

    /// <summary>
    /// </summary>
    /// <param name="fileSpec">a filename with ? when any character can match or * when multiple characters can match. Case is insensitive</param>
    /// <returns></returns>
    public DosFileOperationResult FindFirstMatchingFile(string fileSpec) {
        string hostSearchSpec = ToHostFileName(fileSpec);
        _currentMatchingFileSearchFolder = hostSearchSpec[..(hostSearchSpec.LastIndexOf('/') + 1)];
        if (string.IsNullOrWhiteSpace(_currentMatchingFileSearchFolder) == false) {
            _currentMatchingFileSearchSpec = hostSearchSpec.Replace(_currentMatchingFileSearchFolder, "");
            try {
                List<string> matchingPaths = Directory.GetFiles(
                    _currentMatchingFileSearchFolder,
                    _currentMatchingFileSearchSpec,
                    new EnumerationOptions() {
                        AttributesToSkip = FileAttributes.Directory,
                        IgnoreInaccessible = true,
                        RecurseSubdirectories = false
                    }).ToList();
                _matchingFilesIterator = matchingPaths.GetEnumerator();
                return FindNextMatchingFile();
            } catch (IOException e) {
                e.Demystify();
                if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Error)) {
                    _loggerService.Error(e, "Error while walking path {@CurrentMatchingFileSearchFolder} or getting attributes.", _currentMatchingFileSearchFolder);
                }
            }
        }
        return DosFileOperationResult.Error(ErrorCode.PathNotFound);
    }

    public DosFileOperationResult FindNextMatchingFile() {
        if (_matchingFilesIterator == null) {
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
                _loggerService.Warning("No search was done");
            }
            return FileNotFoundError(null);
        }

        if (!_matchingFilesIterator.MoveNext()) {
            return FileNotFoundErrorWithLog($"No more files matching {_currentMatchingFileSearchSpec} in path {_currentMatchingFileSearchFolder}");
        }

        bool matching = _matchingFilesIterator.MoveNext();
        if (matching) {
            try {
                UpdateDTAFromFile(_matchingFilesIterator.Current);
            } catch (IOException e) {
                e.Demystify();
                if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
                    _loggerService.Warning(e, "Error while getting attributes.");
                }
                return FileNotFoundError(null);
            }
        }

        return DosFileOperationResult.NoValue();
    }

    public ushort DiskTransferAreaAddressOffset => _diskTransferAreaAddressOffset;

    public ushort DiskTransferAreaAddressSegment => _diskTransferAreaAddressSegment;

    public DosFileOperationResult MoveFilePointerUsingHandle(byte originOfMove, ushort fileHandle, uint offset) {
        OpenFile? file = GetOpenFile(fileHandle);
        if (file == null) {
            return FileNotOpenedError(fileHandle);
        }

        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _loggerService.Information("Moving in file {@FileMove}", file.Name);
        }
        Stream randomAccessFile = file.RandomAccessFile;
        try {
            uint newOffset = Seek(randomAccessFile, originOfMove, offset);
            return DosFileOperationResult.Value32(newOffset);
        } catch (IOException e) {
            e.Demystify();
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Error)) {
                _loggerService.Error(e, "An error occurred while seeking file {@Error}", e);
            }
            return DosFileOperationResult.Error(ErrorCode.InvalidHandle);
        }
    }

    public DosFileOperationResult OpenFile(string fileName, byte rwAccessMode) {
        string openMode = _fileOpenMode[rwAccessMode];

        CharacterDevice? device = _dos.Devices.OfType<CharacterDevice>().FirstOrDefault(device => device.Name == fileName);
        if (device is not null) {
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
                _loggerService.Information("Opening device {@FileName} with mode {@OpenMode}", fileName, openMode);
            }
            return OpenDeviceInternal(device, openMode);
        }
        
        string? hostFileName = ToHostCaseSensitiveFileName(fileName, false);
        if (hostFileName == null) {
            return FileNotFoundError(fileName);
        }

        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _loggerService.Information("Opening file {@HostFileName} with mode {@OpenMode}", hostFileName, openMode);
        }

        return OpenFileInternal(fileName, hostFileName, openMode);
    }

    public DosFileOperationResult OpenDevice(CharacterDevice device, string openMode, string name) {
        return OpenDeviceInternal(device, openMode, name);
    }

    private DosFileOperationResult OpenDeviceInternal(CharacterDevice device, string openMode, string? name = null) {
        int? freeIndex = FindNextFreeFileIndex();
        if (freeIndex == null) {
            return NoFreeHandleError();
        }

        Stream stream;
        if (device == _dos.CurrentConsoleDevice) {
             stream = openMode == "r" ? Console.OpenStandardInput() : Console.OpenStandardOutput();
        } else {
            stream = new DeviceStream(device.Name, openMode, _loggerService);
        }

        ushort dosIndex = (ushort)freeIndex.Value;
        SetOpenFile(dosIndex, new OpenFile(name ?? device.Name, dosIndex, stream));
        
        return DosFileOperationResult.Value16(dosIndex);
    }

    public DosFileOperationResult ReadFile(ushort fileHandle, ushort readLength, uint targetAddress) {
        OpenFile? file = GetOpenFile(fileHandle);
        if (file == null) {
            return FileNotOpenedError(fileHandle);
        }

        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _loggerService.Information("Reading from file {@FileName}", file.Name);
        }

        byte[] buffer = new byte[readLength];
        int actualReadLength;
        try {
            actualReadLength = file.RandomAccessFile.Read(buffer, 0, readLength);
        } catch (IOException e) {
            e.Demystify();
            throw new UnrecoverableException("IOException while reading file", e);
        }

        if (actualReadLength == -1) {
            // EOF
            return DosFileOperationResult.Value16(0);
        }

        if (actualReadLength > 0) {
            _memory.LoadData(targetAddress, buffer, actualReadLength);
            file.AddMemoryRange(new MemoryRange(targetAddress, (uint)(targetAddress + actualReadLength - 1), file.Name));
        }

        return DosFileOperationResult.Value16((ushort)actualReadLength);
    }

    public DosFileOperationResult SetCurrentDir(string currentDir) {
        _currentDir = ToHostCaseSensitiveFileName(currentDir, false);
        return DosFileOperationResult.NoValue();
    }

    public void SetDiskParameters(string currentDir, Dictionary<char, string> driveMap) {
        _currentDir = currentDir;
        _driveMap = driveMap;
    }

    public void SetDiskTransferAreaAddress(ushort diskTransferAreaAddressSegment, ushort diskTransferAreaAddressOffset) {
        _diskTransferAreaAddressSegment = diskTransferAreaAddressSegment;
        _diskTransferAreaAddressOffset = diskTransferAreaAddressOffset;
    }

    public DosFileOperationResult WriteFileUsingHandle(ushort fileHandle, ushort writeLength, uint bufferAddress) {
        if (!IsValidFileHandle(fileHandle)) {
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
                _loggerService.Warning("Invalid or unsupported file handle {@FileHandle}. Doing nothing.", fileHandle);
            }

            // Fake that we wrote, this could be used to write to stdout / stderr ...
            return DosFileOperationResult.Value16(writeLength);
        }

        OpenFile? file = GetOpenFile(fileHandle);
        if (file == null) {
            return FileNotOpenedError(fileHandle);
        }

        try {
            // Do not access Ram property directly to trigger breakpoints if needed
            Span<byte> data = _memory.GetSpan((int)bufferAddress, writeLength);
            file.RandomAccessFile.Write(data);
        } catch (IOException e) {
            e.Demystify();
            throw new UnrecoverableException("IOException while writing file", e);
        }

        return DosFileOperationResult.Value16(writeLength);
    }

    private int CountHandles(OpenFile openFileToCount) {
        int count = 0;
        for (int i = 0; i < _openFiles.Length; i++) {
            OpenFile? openFile = _openFiles[i];
            if (openFile == openFileToCount) {
                count++;
            }
        }

        return count;
    }

    private DosFileOperationResult FileNotFoundError(string? fileName) {
        return FileNotFoundErrorWithLog($"File {fileName} not found!");
    }

    private DosFileOperationResult FileNotFoundErrorWithLog(string message) {
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
            _loggerService.Warning("{@FileNotFoundErrorWithLog}: {@Message}", nameof(FileNotFoundErrorWithLog), message);
        }

        return DosFileOperationResult.Error(ErrorCode.FileNotFound);
    }

    private DosFileOperationResult FileNotOpenedError(int fileHandle) {
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
            _loggerService.Warning("File not opened: {@FileHandle}", fileHandle);
        }
        return DosFileOperationResult.Error(ErrorCode.InvalidHandle);
    }

    /// <summary>
    /// Converts a dos filespec to a regex pattern
    /// </summary>
    /// <param name="fileSpec"></param>
    /// <returns></returns>
    private Regex FileSpecToRegex(string fileSpec) {
        string regex = fileSpec.ToLowerInvariant();
        regex = regex.Replace(".", "[.]");
        regex = regex.Replace("?", ".");
        regex = regex.Replace("*", ".*");
        return new Regex(regex);
    }

    private int? FindNextFreeFileIndex() {
        for (int i = 0; i < _openFiles.Length; i++) {
            if (_openFiles[i] == null) {
                return i;
            }
        }

        return null;
    }

    private uint GetDiskTransferAreaAddressPhysical() {
        return MemoryUtils.ToPhysicalAddress(_diskTransferAreaAddressSegment, _diskTransferAreaAddressOffset);
    }

    private OpenFile? GetOpenFile(ushort fileHandle) {
        if (fileHandle >= _openFiles.Length) {
            return null;
        }
        return _openFiles[fileHandle];
    }

    private static bool IsValidFileHandle(ushort fileHandle) {
        return fileHandle <= MaxOpenFiles;
    }

    private DosFileOperationResult NoFreeHandleError() {
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
            _loggerService.Warning("Could not find a free handle {@MethodName}", nameof(NoFreeHandleError));
        }
        return DosFileOperationResult.Error(ErrorCode.TooManyOpenFiles);
    }

    private string? GetActualCaseForFileName(string caseInsensitivePath) {
        string? directory = Path.GetDirectoryName(caseInsensitivePath);
        string? directoryCaseSensitive = GetDirectoryCaseSensitive(directory);
        if (string.IsNullOrWhiteSpace(directoryCaseSensitive) || Directory.Exists(directoryCaseSensitive) == false) {
            return null;
        }
        string realFileName = "";
        string[] array = Directory.GetFiles(directoryCaseSensitive);
        for (int i = 0; i < array.Length; i++) {
            string? file = array[i];
            string? fileToUpper = file.ToUpperInvariant();
            string? searchedFile = caseInsensitivePath.ToUpperInvariant();
            if (fileToUpper == searchedFile) {
                realFileName = file;
            }
        }
        if (string.IsNullOrWhiteSpace(realFileName) || File.Exists(realFileName) == false) {
            return null;
        }
        return realFileName;
    }

    private string? GetDirectoryCaseSensitive(string? directory) {
        if (string.IsNullOrWhiteSpace(directory)) {
            return null;
        }
        DirectoryInfo directoryInfo = new DirectoryInfo(directory);
        if (directoryInfo.Exists) {
            return directory;
        }

        if (directoryInfo.Parent == null) {
            return null;
        }

        string? parent = GetDirectoryCaseSensitive(directoryInfo.Parent.FullName);
        if (parent == null) {
            return null;
        }

        return new DirectoryInfo(parent).GetDirectories(directoryInfo.Name, new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive }).FirstOrDefault()?.FullName;
    }

    private DosFileOperationResult OpenFileInternal(string fileName, string? hostFileName, string openMode) {
        if (hostFileName == null) {
            // Not found
            return FileNotFoundError(fileName);
        }

        int? freeIndex = FindNextFreeFileIndex();
        if (freeIndex == null) {
            return NoFreeHandleError();
        }

        ushort dosIndex = (ushort)freeIndex.Value;
        try {
            Stream? randomAccessFile = null;
            if (openMode == "r") {
                string? realFileName = GetActualCaseForFileName(hostFileName);
                if (File.Exists(hostFileName)) {
                    randomAccessFile = File.OpenRead(hostFileName);
                } else if (File.Exists(realFileName)) {
                    randomAccessFile = File.OpenRead(realFileName);
                } else {
                    return FileNotFoundError(fileName);
                }
            } else if (openMode == "w") {
                randomAccessFile = File.OpenWrite(hostFileName);
            } else if (openMode == "rw") {
                string? realFileName = GetActualCaseForFileName(hostFileName);
                if (File.Exists(hostFileName)) {
                    randomAccessFile = File.Open(hostFileName, FileMode.Open);
                } else if (File.Exists(realFileName)) {
                    randomAccessFile = File.Open(realFileName, FileMode.Open);
                } else {
                    return FileNotFoundError(fileName);
                }
            }
            if (randomAccessFile != null) {
                SetOpenFile(dosIndex, new OpenFile(fileName, dosIndex, randomAccessFile));
            }
        } catch (FileNotFoundException) {
            return FileNotFoundError(fileName);
        }

        return DosFileOperationResult.Value16(dosIndex);
    }

    private string ReplaceDriveWithHostPath(string fileName) {
        // Absolute path
        char driveLetter = fileName.ToUpper()[0];

        if (_driveMap.TryGetValue(driveLetter, out string? pathForDrive) == false) {
            throw new UnrecoverableException($"Could not find a mapping for drive {driveLetter}");
        }

        return fileName.Replace($"{driveLetter}:", pathForDrive);
    }

    private static uint Seek(Stream randomAccessFile, byte originOfMove, uint offset) {
        long newOffset;
        if (originOfMove == 0) {
            newOffset = offset; // seek from beginning, offset is good
        } else if (originOfMove == 1) {
            // seek from last read
            newOffset = randomAccessFile.Position + offset;
        } else {
            // seek from end
            newOffset = randomAccessFile.Length - offset;
        }

        randomAccessFile.Seek(newOffset, SeekOrigin.Begin);
        return (uint)newOffset;
    }

    private void SetOpenFile(ushort fileHandle, OpenFile? openFile) {
        _openFiles[fileHandle] = openFile;
    }

    private string? ToCaseSensitiveFileName(string? caseInsensitivePath) {
        if (string.IsNullOrWhiteSpace(caseInsensitivePath)) {
            return null;
        }

        string fileToProcess = ConvertUtils.ToSlashPath(caseInsensitivePath);
        string? parentDir = Path.GetDirectoryName(fileToProcess);
        if (File.Exists(fileToProcess) || Directory.Exists(fileToProcess) ||
            (string.IsNullOrWhiteSpace(parentDir) == false && Directory.Exists(parentDir) && Directory.GetDirectories(parentDir).Length == 0)) {
            // file exists or root reached, no need to go further. Path found.
            return caseInsensitivePath;
        }

        string? parent = ToCaseSensitiveFileName(parentDir);
        if (parent == null) {
            // End of recursion, root reached
            return null;
        }

        // Now that parent is for sure on the disk, let's find the current file
        try {
            string? fileNameOnFileSystem = GetActualCaseForFileName(caseInsensitivePath);
            if (string.IsNullOrWhiteSpace(fileNameOnFileSystem) == false) {
                return fileNameOnFileSystem;
            }
            Regex fileToProcessRegex = FileSpecToRegex(Path.GetFileName(fileToProcess));
            if (Directory.Exists(parent)) {
                return Array.Find(Directory
                    .GetFiles(parent), x => fileToProcessRegex.IsMatch(x));
            }
        } catch (IOException e) {
            e.Demystify();
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
                _loggerService.Warning(e, "Error while checking file {@CaseInsensitivePath}: {@Exception}", caseInsensitivePath, e);
            }
        }

        return null;
    }

    private static ushort ToDosDate(DateTime localDate) {
        // https://stanislavs.org/helppc/file_attributes.html
        int day = localDate.Day;
        int month = localDate.Month;
        int dosYear = localDate.Year - 1980;
        return (ushort)((day & 0b11111) | (month & 0b1111) << 5 | (dosYear & 0b1111111) << 9);
    }

    private static ushort ToDosTime(DateTime localTime) {
        // https://stanislavs.org/helppc/file_attributes.html
        int dosSeconds = localTime.Second / 2;
        int minutes = localTime.Minute;
        int hours = localTime.Hour;
        return (ushort)((dosSeconds & 0b11111) | (minutes & 0b111111) << 5 | (hours & 0b11111) << 11);
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
    /// <param name="forCreation">if true will try to find case sensitive match for only the parent of the file</param>
    /// <returns>the file name in the host file system, or null if nothing was found.</returns>
    public string? ToHostCaseSensitiveFileName(string dosFileName, bool forCreation) {
        string fileName = ToHostFileName(dosFileName);
        if (!forCreation) {
            return ToCaseSensitiveFileName(fileName);
        }
        string? parent = ToCaseSensitiveFileName(Path.GetDirectoryName(fileName));
        if (parent == null) {
            return null;
        }
        // Concat the folder to the requested file name
        return Path.Combine(parent, dosFileName);
    }

    /// <summary>
    /// Prefixes the given filename by either the mapped drive folder or the current folder depending on whether there is
    /// a Drive in the filename or not.<br/>
    /// Does not convert to case sensitive filename.
    /// </summary>
    /// <param name="dosFileName"></param>
    /// <returns></returns>
    private string ToHostFileName(string dosFileName) {
        string fileName = ConvertUtils.ToSlashPath(dosFileName);
        if (fileName.Length >= 2 && fileName[1] == ':') {
            fileName = ReplaceDriveWithHostPath(fileName);
        } else if (string.IsNullOrWhiteSpace(_currentDir) == false) {
            fileName = Path.Combine(_currentDir, fileName);
        }

        return ConvertUtils.ToSlashPath(fileName);
    }

    private void UpdateDTAFromFile(string matchingFile) {
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _loggerService.Information("Found matching file {@MatchingFile}", matchingFile);
        }
        DosDiskTransferArea dosDiskTransferArea = new(_memory, GetDiskTransferAreaAddressPhysical());
        FileInfo attributes = new FileInfo(matchingFile);
        DateTime creationZonedDateTime = attributes.CreationTimeUtc;
        DateTime creationLocalDate = creationZonedDateTime.ToLocalTime();
        DateTime creationLocalTime = creationZonedDateTime.ToLocalTime();
        dosDiskTransferArea.FileDate = ToDosDate(creationLocalDate);
        dosDiskTransferArea.FileTime = ToDosTime(creationLocalTime);
        dosDiskTransferArea.FileSize = (ushort)attributes.Length;
        dosDiskTransferArea.FileName = Path.GetFileName(matchingFile);
    }
}