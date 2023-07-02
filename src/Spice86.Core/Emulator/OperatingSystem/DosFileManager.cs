
namespace Spice86.Core.Emulator.OperatingSystem;

using System.Linq;
using System.Diagnostics;

using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem.Devices;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Emulator.Errors;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

/// <summary>
/// The class that implements DOS file operations, such as finding files, allocating file handles, and updating the Dos Transfer Area.
/// </summary>
public class DosFileManager {
    private const int MaxOpenFiles = 20;
    private static readonly Dictionary<byte, string> FileOpenMode = new();
    private readonly ILoggerService _loggerService;

    private string? _currentMatchingFileSearchFolder;

    private string? _currentMatchingFileSearchSpec;

    private ushort _diskTransferAreaAddressOffset;

    private ushort _diskTransferAreaAddressSegment;

    private IEnumerator<string>? _matchingFilesIterator;

    private readonly IMemory _memory;

    private readonly IDosPathResolver _dosPathResolver;

    /// <summary>
    /// All the files opened by DOS.
    /// </summary>
    public OpenFile?[] OpenFiles { get; } = new OpenFile[MaxOpenFiles];
    
    private readonly IList<IVirtualDevice> _dosVirtualDevices;

    static DosFileManager() {
        FileOpenMode.Add(0x00, "r");
        FileOpenMode.Add(0x01, "w");
        FileOpenMode.Add(0x02, "rw");
    }


    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    /// <param name="dosVirtualDevices">The virtual devices from the DOS kernel.</param>
    /// <param name="dosFilePathResolver">The class used to resolve DOS paths to host paths.</param>
    public DosFileManager(IMemory memory, ILoggerService loggerService, IList<IVirtualDevice> dosVirtualDevices, IDosPathResolver dosFilePathResolver) {
        _loggerService = loggerService;
        _dosPathResolver = dosFilePathResolver;
        _memory = memory;
        _dosVirtualDevices = dosVirtualDevices;
    }

    /// <summary>
    /// Closes a file handle.
    /// </summary>
    /// <param name="fileHandle">The file handle to an open file.</param>
    /// <returns>A <see cref="DosFileOperationResult"/> with details about the result of the operation.</returns>
    /// <exception cref="UnrecoverableException">If the host OS refuses to close the file.</exception>
    public DosFileOperationResult CloseFile(ushort fileHandle) {
        OpenFile? file = GetOpenFile(fileHandle);
        if (file == null) {
            return FileNotOpenedError(fileHandle);
        }

        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Debug)) {
            _loggerService.Debug("Closed {ClosedFileName}, file was loaded in ram in those addresses: {ClosedFileAddresses}", file.Name, file.LoadedMemoryRanges);
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

    /// <summary>
    /// Creates a file and returns the handle to the file.
    /// </summary>
    /// <param name="fileName">The target file name.</param>
    /// <param name="fileAttribute">The file system attributes to set on the new file.</param>
    /// <returns>A <see cref="DosFileOperationResult"/> with details about the result of the operation.</returns>
    /// <exception cref="UnrecoverableException"></exception>
    public DosFileOperationResult CreateFileUsingHandle(string fileName, ushort fileAttribute) {
        string? hostFileName = _dosPathResolver.ToHostCaseSensitiveFullName( fileName, true);
        if (string.IsNullOrWhiteSpace(hostFileName)) {
            return FileOperationErrorWithLog($"Could not find parent of {fileName} so cannot create file.", ErrorCode.PathNotFound);
        }

        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("Creating file {HostFileName} with attribute {FileAttribute}", hostFileName, fileAttribute);
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

    /// <summary>
    /// Gets another file handle for the same file
    /// </summary>
    /// <param name="fileHandle">The handle to a file.</param>
    /// <returns>A <see cref="DosFileOperationResult"/> with details about the result of the operation.</returns>
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
    /// Returns the first matching file according to the <paramref name="fileSpec"/>
    /// </summary>
    /// <param name="fileSpec">a filename with ? when any character can match or * when multiple characters can match. Case is insensitive</param>
    /// <returns>A <see cref="DosFileOperationResult"/> with details about the result of the operation.</returns>
    public DosFileOperationResult FindFirstMatchingFile(string fileSpec) {
        string hostSearchSpec = _dosPathResolver.PrefixWithHostDirectory(fileSpec);
        _currentMatchingFileSearchFolder = _dosPathResolver.GetFullNameForParentDirectory(hostSearchSpec);
        if (string.IsNullOrWhiteSpace(_currentMatchingFileSearchFolder)) {
            return DosFileOperationResult.Error(ErrorCode.PathNotFound);
        }
        
        if (!Directory.Exists(_currentMatchingFileSearchFolder)) {
            return DosFileOperationResult.Error(ErrorCode.PathNotFound);
        }
        
        _currentMatchingFileSearchSpec = Path.GetRelativePath(_currentMatchingFileSearchFolder, hostSearchSpec);

        try {
            List<string> matchingPaths = Directory.GetFiles(
                _currentMatchingFileSearchFolder,
                _currentMatchingFileSearchSpec,
                new EnumerationOptions {
                    AttributesToSkip = FileAttributes.Directory,
                    IgnoreInaccessible = true,
                    RecurseSubdirectories = false
                }).ToList();
            _matchingFilesIterator = matchingPaths.GetEnumerator();
            return FindNextMatchingFile();
        } catch (IOException e) {
            e.Demystify();
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Error)) {
                _loggerService.Error(e, "Error while walking path {CurrentMatchingFileSearchFolder} or getting attributes", _currentMatchingFileSearchFolder);
            }
        }
        return DosFileOperationResult.Error(ErrorCode.PathNotFound);
    }

    /// <summary>
    /// Returns the first matching file according to the file spec.
    /// </summary>
    /// <returns>A <see cref="DosFileOperationResult"/> with details about the result of the operation.</returns>
    public DosFileOperationResult FindNextMatchingFile() {
        if (_matchingFilesIterator == null) {
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
                _loggerService.Warning("No search was done");
            }
            return FileNotFoundError(null);
        }

        if (!_matchingFilesIterator.MoveNext()) {
            return FileOperationErrorWithLog($"No more files matching {_currentMatchingFileSearchSpec} in path {_currentMatchingFileSearchFolder}", ErrorCode.NoMoreMatchingFiles);
        }

        bool matching = _matchingFilesIterator.MoveNext();
        if (matching) {
            try {
                UpdateDosTransferAreaFromFile(_matchingFilesIterator.Current);
            } catch (IOException e) {
                e.Demystify();
                if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
                    _loggerService.Warning(e, "Error while getting attributes");
                }
                return FileNotFoundError(null);
            }
        }

        return DosFileOperationResult.NoValue();
    }

    /// <summary>
    /// The offset part of the segmented address to the DTA.
    /// </summary>
    public ushort DiskTransferAreaAddressOffset => _diskTransferAreaAddressOffset;

    /// <summary>
    /// The segment part of the segmented address to the DTA.
    /// </summary>
    public ushort DiskTransferAreaAddressSegment => _diskTransferAreaAddressSegment;

    /// <summary>
    /// Seeks to specified location in file.
    /// </summary>
    /// <param name="originOfMove">Can be one of those values: <br/>
    /// 00 = beginning of file plus offset  (SEEK_SET) <br/>
    /// 01 = current location plus offset	(SEEK_CUR) <br/>
    /// 02 = end of file plus offset  (SEEK_END)
    /// </param>
    /// <param name="fileHandle">The handle to the file.</param>
    /// <param name="offset">Number of bytes to move.</param>
    /// <returns>A <see cref="DosFileOperationResult"/> with details about the result of the operation.</returns>
    public DosFileOperationResult MoveFilePointerUsingHandle(byte originOfMove, ushort fileHandle, uint offset) {
        OpenFile? file = GetOpenFile(fileHandle);
        if (file == null) {
            return FileNotOpenedError(fileHandle);
        }

        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("Moving in file {FileMove}", file.Name);
        }
        Stream randomAccessFile = file.RandomAccessFile;
        try {
            uint newOffset = Seek(randomAccessFile, originOfMove, offset);
            return DosFileOperationResult.Value32(newOffset);
        } catch (IOException e) {
            e.Demystify();
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Error)) {
                _loggerService.Error(e, "An error occurred while seeking file {Error}", e);
            }
            return DosFileOperationResult.Error(ErrorCode.InvalidHandle);
        }
    }

    /// <summary>
    /// Opens a file (in read+write access mode) and returns the file handle.
    /// </summary>
    /// <param name="fileName">The name of the file to open.</param>
    /// <param name="rwAccessMode">The read+write access mode</param>
    /// <returns>A <see cref="DosFileOperationResult"/> with details about the result of the operation.</returns>
    public DosFileOperationResult OpenFile(string fileName, byte rwAccessMode) {
        string openMode = FileOpenMode[rwAccessMode];

        CharacterDevice? device = _dosVirtualDevices.OfType<CharacterDevice>().FirstOrDefault(device => device.Name == fileName);
        if (device is not null) {
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
                _loggerService.Verbose("Opening device {FileName} with mode {OpenMode}", fileName, openMode);
            }
            return OpenDeviceInternal(device, openMode);
        }
        
        string? hostFileName = _dosPathResolver.ToHostCaseSensitiveFullName( fileName, false);
        if (hostFileName == null) {
            return FileNotFoundError(fileName);
        }

        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Debug)) {
            _loggerService.Debug("Opening file {HostFileName} with mode {OpenMode}", hostFileName, openMode);
        }

        return OpenFileInternal(fileName, hostFileName, openMode);
    }

    /// <summary>
    /// Returns a handle to a DOS <see cref="CharacterDevice"/>
    /// </summary>
    /// <param name="device">The character device</param>
    /// <param name="openMode">Open in Read, Write, or Read+Write mode.</param>
    /// <param name="name">The name of the device, such as "STDIN" for the standard input.</param>
    /// <returns>A <see cref="DosFileOperationResult"/> with details about the result of the operation.</returns>
    public DosFileOperationResult OpenDevice(CharacterDevice device, string openMode, string name) {
        return OpenDeviceInternal(device, openMode, name);
    }

    private DosFileOperationResult OpenDeviceInternal(CharacterDevice device, string openMode, string? name = null) {
        int? freeIndex = FindNextFreeFileIndex();
        if (freeIndex == null) {
            return NoFreeHandleError();
        }

        Stream stream = device.OpenStream(openMode);
        ushort dosIndex = (ushort)freeIndex.Value;
        SetOpenFile(dosIndex, new OpenFile(name ?? device.Name, dosIndex, stream));
        
        return DosFileOperationResult.Value16(dosIndex);
    }

    /// <summary>
    /// Read a file using a handle
    /// </summary>
    /// <param name="fileHandle">The handle to the file.</param>
    /// <param name="readLength">The amount of data to read.</param>
    /// <param name="targetAddress">The start address of the receiving buffer.</param>
    /// <returns></returns>
    /// <returns>A <see cref="DosFileOperationResult"/> with details about the result of the operation.</returns>
    public DosFileOperationResult ReadFile(ushort fileHandle, ushort readLength, uint targetAddress) {
        OpenFile? file = GetOpenFile(fileHandle);
        if (file == null) {
            return FileNotOpenedError(fileHandle);
        }

        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("Reading from file {FileName}", file.Name);
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

    /// <summary>
    /// Sets the current directory for the <see cref="DosFileManager"/>
    /// </summary>
    /// <param name="newPath">The new current directory path</param>
    /// <returns>A <see cref="DosFileOperationResult"/> with details about the result of the operation.</returns>
    public DosFileOperationResult SetCurrentDir(string newPath) => _dosPathResolver.SetCurrentDir(newPath);

    /// <summary>
    /// Initializes disk parameters
    /// </summary>
    /// <param name="currentDrive">The current DOS drive letter.</param>
    /// <param name="dosPath">The current host directory on the DOS drive.</param>
    /// <param name="driveMap">The mapping between emulated drive roots and host directory paths.</param>
    public void SetDiskParameters(char currentDrive, string dosPath, Dictionary<char, MountedFolder> driveMap) => _dosPathResolver.SetDiskParameters(currentDrive, dosPath, driveMap);

    /// <summary>
    /// Sets the segmented address to the DTA.
    /// </summary>
    /// <param name="diskTransferAreaAddressSegment">The segment part of the segmented address to the DTA.</param>
    /// <param name="diskTransferAreaAddressOffset">The offset part of the segmented address to the DTA.</param>
    public void SetDiskTransferAreaAddress(ushort diskTransferAreaAddressSegment, ushort diskTransferAreaAddressOffset) {
        _diskTransferAreaAddressSegment = diskTransferAreaAddressSegment;
        _diskTransferAreaAddressOffset = diskTransferAreaAddressOffset;
    }

    /// <summary>
    /// Writes data to a file using a file handle.
    /// </summary>
    /// <param name="fileHandle">The file handle to use.</param>
    /// <param name="writeLength">The length of the data to write.</param>
    /// <param name="bufferAddress">The address of the buffer containing the data to write.</param>
    /// <returns>A <see cref="DosFileOperationResult"/> object representing the result of the operation.</returns>
    public DosFileOperationResult WriteFileUsingHandle(ushort fileHandle, ushort writeLength, uint bufferAddress) {
        if (!IsValidFileHandle(fileHandle)) {
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
                _loggerService.Warning("Invalid or unsupported file handle {FileHandle}. Doing nothing", fileHandle);
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

    private int CountHandles(OpenFile openFileToCount) => OpenFiles.Count(openFile => openFile == openFileToCount);

    private DosFileOperationResult FileAccessDeniedError(string? filename) {
        return FileOperationErrorWithLog($"File {filename} already in use!", ErrorCode.AccessDenied);
    }

    private DosFileOperationResult FileNotFoundError(string? fileName) {
        return FileOperationErrorWithLog($"File {fileName} not found!", ErrorCode.FileNotFound);
    }

    private DosFileOperationResult FileOperationErrorWithLog(string message, ErrorCode errorCode) {
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
            _loggerService.Warning("{FileNotFoundErrorWithLog}: {Message}", nameof(FileOperationErrorWithLog), message);
        }

        return DosFileOperationResult.Error(errorCode);
    }

    private DosFileOperationResult FileNotOpenedError(int fileHandle) {
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
            _loggerService.Warning("File not opened: {FileHandle}", fileHandle);
        }
        return DosFileOperationResult.Error(ErrorCode.InvalidHandle);
    }

    private int? FindNextFreeFileIndex() {
        for (int i = 0; i < OpenFiles.Length; i++) {
            if (OpenFiles[i] == null) {
                return i;
            }
        }

        return null;
    }

    private uint GetDiskTransferAreaAddressPhysical() => MemoryUtils.ToPhysicalAddress(_diskTransferAreaAddressSegment, _diskTransferAreaAddressOffset);

    private OpenFile? GetOpenFile(ushort fileHandle) {
        if (fileHandle >= OpenFiles.Length) {
            return null;
        }
        return OpenFiles[fileHandle];
    }

    private static bool IsValidFileHandle(ushort fileHandle) => fileHandle <= MaxOpenFiles;

    private DosFileOperationResult NoFreeHandleError() {
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
            _loggerService.Warning("Could not find a free handle {MethodName}", nameof(NoFreeHandleError));
        }
        return DosFileOperationResult.Error(ErrorCode.TooManyOpenFiles);
    }
    
    /// <summary>
    /// Converts dosFileName to a host file name.<br/>
    /// For this, this needs to:
    /// <ul>
    /// <li>Prefix either the current folder or the drive folder.</li>
    /// <li>Replace backslashes with slashes</li>
    /// <li>Find case sensitive matches for every path item (since DOS is case insensitive but some OS are not)</li>
    /// </ul>
    /// </summary>
    /// <param name="dosFileName">The file name to convert.</param>
    /// <param name="convertParentOnly">if true will try to find case sensitive match for only the parent path</param>
    /// <returns>A string containing the file name in the host file system, or <c>null</c> if nothing was found.</returns>
    internal string? ToHostCaseSensitiveFileName(string dosFileName, bool convertParentOnly) => _dosPathResolver.ToHostCaseSensitiveFullName( dosFileName, convertParentOnly);

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
            switch (openMode) {
                case "r": {
                    string? realFileName = _dosPathResolver.TryGetFullHostFileName(hostFileName);
                    if (File.Exists(hostFileName)) {
                        randomAccessFile = File.OpenRead(hostFileName);
                    } else if (File.Exists(realFileName)) {
                        randomAccessFile = File.OpenRead(realFileName);
                    } else {
                        return FileNotFoundError(fileName);
                    }

                    break;
                }
                case "w":
                    randomAccessFile = File.OpenWrite(hostFileName);
                    break;
                case "rw": {
                    string? realFileName = _dosPathResolver.TryGetFullHostFileName(hostFileName);
                    if (File.Exists(hostFileName)) {
                        randomAccessFile = File.Open(hostFileName, FileMode.Open);
                    } else if (File.Exists(realFileName)) {
                        randomAccessFile = File.Open(realFileName, FileMode.Open);
                    } else {
                        return FileNotFoundError(fileName);
                    }

                    break;
                }
            }

            if (randomAccessFile != null) {
                SetOpenFile(dosIndex, new OpenFile(fileName, dosIndex, randomAccessFile));
            }
        } catch (FileNotFoundException) {
            return FileNotFoundError(fileName);
        } catch (IOException) {
            return FileAccessDeniedError(fileName);
        }

        return DosFileOperationResult.Value16(dosIndex);
    }

    private static uint Seek(Stream randomAccessFile, byte originOfMove, uint offset) {
        long newOffset = originOfMove switch {
            0 => offset,
            1 => randomAccessFile.Position + offset,
            _ => randomAccessFile.Length - offset
        };

        randomAccessFile.Seek(newOffset, SeekOrigin.Begin);
        return (uint)newOffset;
    }

    private void SetOpenFile(ushort fileHandle, OpenFile? openFile) => OpenFiles[fileHandle] = openFile;
    
    private void UpdateDosTransferAreaFromFile(string matchingFile) {
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("Found matching file {MatchingFile}", matchingFile);
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

    /// <summary>
    /// Creates a directory on disk.
    /// </summary>
    /// <param name="dosDirectory">The directory name to create</param>
    /// <returns></returns>
    /// <returns>A <see cref="DosFileOperationResult"/> with details about the result of the operation.</returns>
    public DosFileOperationResult CreateDirectory(string dosDirectory) {
         string? fullPath = _dosPathResolver.ToHostCaseSensitiveFullName(dosDirectory, true);
        if (string.IsNullOrWhiteSpace(fullPath)) {
            return FileNotFoundError(dosDirectory);
        }

        string parentFolder = _dosPathResolver.GetFullNameForParentDirectory(fullPath);

        if (!Directory.Exists(parentFolder)) {
            CreateDirectory(_dosPathResolver.GetHostRelativePathToCurrentDirectory(parentFolder));
        }

        if (_dosPathResolver.IsThereAnyDirectoryOrFileWithTheSameName(dosDirectory, new DirectoryInfo(parentFolder))) {
            return FileNotFoundError(dosDirectory);
        }

        string hostPath = _dosPathResolver.PrefixWithHostDirectory(dosDirectory);
        try {
            Directory.CreateDirectory(hostPath);
            return DosFileOperationResult.NoValue();
        } catch (IOException e) {
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
                _loggerService.Warning(e, "Error while creating directory {CaseInsensitivePath}: {Exception}",
                    hostPath, e);
            }
        }

        return FileNotFoundError(dosDirectory);
    }
}