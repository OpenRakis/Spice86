
namespace Spice86.Core.Emulator.OperatingSystem;

using Serilog.Events;

using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem.Devices;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Emulator.Errors;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System.Diagnostics.CodeAnalysis;
using System.Linq;

/// <summary>
/// The class that implements DOS file operations, such as finding files, allocating file handles, and updating the Disk Transfer Area.
/// </summary>
public class DosFileManager {
    private static readonly char[] _directoryChars = { DosPathResolver.DirectorySeparatorChar, DosPathResolver.AltDirectorySeparatorChar };
    private const int MaxOpenFiles = 20;
    private static readonly Dictionary<byte, string> FileOpenMode = new();
    private readonly ILoggerService _loggerService;

    private ushort _diskTransferAreaAddressOffset;

    private ushort _diskTransferAreaAddressSegment;

    private readonly IMemory _memory;

    private readonly DosPathResolver _dosPathResolver;

    private readonly Dictionary<byte, (string FileSystemEntry, string FileSpec)> _activeFileSearches = new();

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
    /// <param name="cDriveFolderPath">The host path to be mounted as C:.</param>
    /// <param name="executablePath">The host path to the DOS executable to be launched.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    /// <param name="dosVirtualDevices">The virtual devices from the DOS kernel.</param>
    public DosFileManager(IMemory memory, string? cDriveFolderPath, string? executablePath, ILoggerService loggerService, IList<IVirtualDevice> dosVirtualDevices) {
        _loggerService = loggerService;
        _dosPathResolver = new(cDriveFolderPath, executablePath);
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

        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("Closed {ClosedFileName}, file was loaded in ram in those addresses: {ClosedFileAddresses}", file.Name, file.LoadedMemoryRanges);
        }

        SetOpenFile(fileHandle, null);
        try {
            if (CountHandles(file) == 0) {
                // Only close the file if no other handle to it exist.
                file.RandomAccessFile.Close();
            }
        } catch (IOException e) {
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
        string prefixedPath = _dosPathResolver.PrefixWithHostDirectory(fileName);

        FileStream? testFileStream = null;
        try {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("Creating file using handle: {PrefixedPath} with {Attributes}", prefixedPath, fileAttribute);
            }
            if (File.Exists(prefixedPath)) {
                File.Delete(prefixedPath);
            }

            testFileStream = File.Create(prefixedPath);
            File.SetAttributes(prefixedPath, (FileAttributes)fileAttribute);
        } catch (IOException e) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning(e, "Error while creating a file using a handle with {FileName} and {FileAttribute}", fileName, fileAttribute);
            }
            return PathNotFoundError(fileName);
        } finally {
            testFileStream?.Dispose();
        }

        return OpenFileInternal(fileName, prefixedPath, "rw");
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
    /// Returns the first matching file in the DTA, according to the <paramref name="fileSpec"/>
    /// </summary>
    /// <param name="fileSpec">a filename with ? when any character can match or * when multiple characters can match. Case is insensitive</param>
    /// <param name="searchAttributes">The MS-DOS file attributes, such as Directory.</param>
    /// <returns>A <see cref="DosFileOperationResult"/> with details about the result of the operation.</returns>
    public DosFileOperationResult FindFirstMatchingFile(string fileSpec, ushort searchAttributes) {
        if (string.IsNullOrWhiteSpace(fileSpec)) {
            return PathNotFoundError(fileSpec);
        }

        DosDiskTransferArea dta = GetDosDiskTransferArea();
        dta.SearchId = GenerateNewKey();
        dta.Drive = DefaultDrive;
        dta.EntryCountWithinSearchResults = 0;

        if (_dosVirtualDevices.OfType<CharacterDevice>().SingleOrDefault(x => x.Name.Equals(fileSpec, StringComparison.OrdinalIgnoreCase)) is { } characterDevice) {
            if(!TryUpdateDosTransferAreaWithFileMatch(dta, characterDevice.Name, out DosFileOperationResult status, searchAttributes)) {
                return status;
            }
            _activeFileSearches.Add(dta.SearchId, (characterDevice.Name, fileSpec));
            return DosFileOperationResult.NoValue();
        }

        if (IsOnlyADosDriveRoot(fileSpec)) {
            return DosFileOperationResult.Error(ErrorCode.NoMoreFiles);
        }

        if (!GetSearchFolder(fileSpec, out string? searchFolder)) {
            return DosFileOperationResult.Error(ErrorCode.PathNotFound);
        }

        EnumerationOptions enumerationOptions = GetEnumerationOptions(searchAttributes);

        try {
            string searchPattern = GetFileSpecWithoutSubFolderOrDriveInIt(fileSpec) ?? fileSpec;
            string[] matchingPaths = Directory.GetFileSystemEntries(
                searchFolder,
                searchPattern,
                enumerationOptions);

            if (matchingPaths.Length == 0) {
                return DosFileOperationResult.Error(ErrorCode.PathNotFound);
            }

            if (!TryUpdateDosTransferAreaWithFileMatch(dta, matchingPaths[0], out DosFileOperationResult status, searchAttributes)) {
                return status;
            }

            _activeFileSearches.Add(dta.SearchId, (matchingPaths[0], fileSpec));
            return DosFileOperationResult.NoValue();

        } catch (Exception e) when (e is UnauthorizedAccessException or IOException or PathTooLongException or DirectoryNotFoundException or ArgumentException) {
            if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                _loggerService.Error(e, "Error while walking path {SearchFolder} or getting attributes", searchFolder);
            }
        }
        return DosFileOperationResult.Error(ErrorCode.PathNotFound);
    }

    private byte GenerateNewKey() {
        if (_activeFileSearches.Count == 0) {
            return 0;
        }

        return (byte)_activeFileSearches.Keys.Count;
    }

    private bool GetSearchFolder(string fileSpec, [NotNullWhen(true)] out string? searchFolder) {
        string subFolderName = GetSubFoldersInFileSpec(fileSpec) ?? ".";
        searchFolder = _dosPathResolver.GetFullHostPathFromDosOrDefault(subFolderName);
        return !string.IsNullOrWhiteSpace(searchFolder);
    }

    private static string? GetSubFoldersInFileSpec(string fileSpec) {
        int index = fileSpec.LastIndexOfAny(_directoryChars);
        if (index != -1) {
            return fileSpec[..index];
        }
        return null;
    }


    private static string? GetFileSpecWithoutSubFolderOrDriveInIt(string fileSpec) {
        int index = fileSpec.LastIndexOfAny(_directoryChars);
        if(index == -1){
            index = fileSpec.LastIndexOfAny([DosPathResolver.VolumeSeparatorChar]);
        }
        if (index != -1){
            int indexIncludingDirChar = index + 1;
            return fileSpec[indexIncludingDirChar..];
        }
        return null;
    }

    private void UpdateActiveSearch(byte key, string matchingFileSystemEntryName, string fileSpec) {
        if (_activeFileSearches.TryGetValue(key, out (string FileSystemEntry, string FileSpec) search)) {
            search.FileSystemEntry = matchingFileSystemEntryName;
            search.FileSpec = fileSpec;
            _activeFileSearches[key] = search;
        }
    }

    private static bool IsOnlyADosDriveRoot(string filePath) => filePath.Length == 3 && (filePath[2] == DosPathResolver.DirectorySeparatorChar || filePath[2] == DosPathResolver.AltDirectorySeparatorChar);

    private EnumerationOptions GetEnumerationOptions(ushort attributes) {
        EnumerationOptions enumerationOptions = new() {
            IgnoreInaccessible = true,
            RecurseSubdirectories = false,
            MatchCasing = MatchCasing.CaseInsensitive,
            MatchType = MatchType.Win32
        };

        if (attributes == 0) {
            enumerationOptions.AttributesToSkip = FileAttributes.System | FileAttributes.Hidden;
            return enumerationOptions;
        }

        DosFileAttributes attributesToInclude = (DosFileAttributes)attributes;

        FileAttributes attributesToSkip = 0;

        if (!attributesToInclude.HasFlag(DosFileAttributes.Directory)) {
            attributesToSkip |= FileAttributes.Directory;
        }
        if (!attributesToInclude.HasFlag(DosFileAttributes.System)) {
            attributesToSkip |= FileAttributes.System;
        }
        if (!attributesToInclude.HasFlag(DosFileAttributes.Hidden)) {
            attributesToSkip |= FileAttributes.Hidden;
        }

        enumerationOptions.AttributesToSkip = attributesToSkip;

        return enumerationOptions;
    }

    /// <summary>
    /// Returns the next matching file in the DTA, according to the stored file spec.
    /// </summary>
    /// <returns>A <see cref="DosFileOperationResult"/> with details about the result of the operation.</returns>
    public DosFileOperationResult FindNextMatchingFile() {
        DosDiskTransferArea dta = GetDosDiskTransferArea();
        ushort searchDrive = dta.Drive;
        if (searchDrive >= 26 || searchDrive > _dosPathResolver.NumberOfPotentiallyValidDriveLetters) {
            return FileOperationErrorWithLog("Search on an invalid drive", ErrorCode.NoMoreMatchingFiles);
        }

        byte key = dta.SearchId;
        if (!_activeFileSearches.TryGetValue(key, out (string FileSystemEntry, string FileSpec) search)) {
            return FileOperationErrorWithLog($"Call FindFirst first to initiate a search.", ErrorCode.NoMoreMatchingFiles);
        }

        if (!GetSearchFolder(search.FileSpec, out string? searchFolder)) {
            return FileOperationErrorWithLog("Search in an invalid folder", ErrorCode.NoMoreMatchingFiles);
        }

        string[] matchingFiles = Directory.GetFileSystemEntries(searchFolder, GetFileSpecWithoutSubFolderOrDriveInIt(search.FileSpec) ?? search.FileSpec, GetEnumerationOptions(dta.SearchAttributes));

        if (matchingFiles.Length == 0 || dta.EntryCountWithinSearchResults >= matchingFiles.Length ||
            (!File.Exists(search.FileSystemEntry) && !Directory.Exists(search.FileSystemEntry))) {
            return FileOperationErrorWithLog($"No more files matching for {search.FileSpec} in path {searchFolder}", ErrorCode.NoMoreMatchingFiles);
        }

        string matchFileSystemEntryName = matchingFiles[dta.EntryCountWithinSearchResults..][0];
        dta.EntryCountWithinSearchResults++;

        if (!TryUpdateDosTransferAreaWithFileMatch(dta, matchFileSystemEntryName, out _)) {
            return FileOperationErrorWithLog("Error when getting file system entry attributes of FindNext match.", ErrorCode.NoMoreMatchingFiles);
        }
        UpdateActiveSearch(key, matchFileSystemEntryName, search.FileSpec);

        return DosFileOperationResult.NoValue();
    }

    private bool TryUpdateDosTransferAreaWithFileMatch(DosDiskTransferArea dta, string filename, out DosFileOperationResult status, ushort? searchAttributes = null){
        try {
            UpdateDosTransferAreaWithFileMatch(dta, filename, searchAttributes);
        }
        catch (IOException e) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)){
                _loggerService.Warning(e, "Error while getting attributes");
            }
            status = FileNotFoundError(null);
            return false;
        }
        status = DosFileOperationResult.NoValue();
        return true;
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

        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("Moving in file {FileMove}", file.Name);
        }
        Stream randomAccessFile = file.RandomAccessFile;
        try {
            uint newOffset = Seek(randomAccessFile, originOfMove, offset);
            return DosFileOperationResult.Value32(newOffset);
        } catch (IOException e) {
            if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                _loggerService.Error(e, "An error occurred while seeking file {Error}", e);
            }
            return DosFileOperationResult.Error(ErrorCode.InvalidHandle);
        }
    }

    /// <summary>
    /// Opens a file and returns the file handle.
    /// </summary>
    /// <param name="fileName">The name of the file to open.</param>
    /// <param name="accessMode">The access mode (read, write, or read+write)</param>
    /// <returns>A <see cref="DosFileOperationResult"/> with details about the result of the operation.</returns>
    public DosFileOperationResult OpenFile(string fileName, byte accessMode) {
        string openMode = FileOpenMode[accessMode];

        CharacterDevice? device = _dosVirtualDevices.OfType<CharacterDevice>().FirstOrDefault(device => device.Name == fileName);
        if (device is not null) {
            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                _loggerService.Verbose("Opening device {FileName} with mode {OpenMode}", fileName, openMode);
            }
            return OpenDeviceInternal(device, openMode);
        }

        string? hostFileName = _dosPathResolver.GetFullHostPathFromDosOrDefault(fileName);
        if (string.IsNullOrWhiteSpace(hostFileName)) {
            return FileNotFoundError($"'{fileName}'");
        }

        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
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
    /// <returns>A <see cref="DosFileOperationResult"/> with details about the result of the operation.</returns>
    public DosFileOperationResult ReadFile(ushort fileHandle, ushort readLength, uint targetAddress) {
        OpenFile? file = GetOpenFile(fileHandle);
        if (file == null) {
            return FileNotOpenedError(fileHandle);
        }

        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("Reading from file {FileName}", file.Name);
        }

        byte[] buffer = new byte[readLength];
        int actualReadLength;
        try {
            actualReadLength = file.RandomAccessFile.Read(buffer, 0, readLength);
        } catch (IOException e) {
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
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
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

    private DosFileOperationResult PathNotFoundError(string? path) {
        return FileOperationErrorWithLog($"File {path} not found!", ErrorCode.PathNotFound);
    }

    private DosFileOperationResult RemoveCurrentDirError(string? path) {
        return FileOperationErrorWithLog($"Attempted to remove current dir {path}", ErrorCode.AttemptedToRemoveCurrentDirectory);
    }

    private DosFileOperationResult FileOperationErrorWithLog(string message, ErrorCode errorCode) {
        if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
            _loggerService.Warning("{FileNotFoundErrorWithLog}: {Message}", nameof(FileOperationErrorWithLog), message);
        }

        return DosFileOperationResult.Error(errorCode);
    }

    private DosFileOperationResult FileNotOpenedError(int fileHandle) {
        if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
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

    private uint GetDiskTransferAreaPhysicalAddress() => MemoryUtils.ToPhysicalAddress(_diskTransferAreaAddressSegment, _diskTransferAreaAddressOffset);

    private OpenFile? GetOpenFile(ushort fileHandle) {
        if (fileHandle >= OpenFiles.Length) {
            return null;
        }
        return OpenFiles[fileHandle];
    }

    private static bool IsValidFileHandle(ushort fileHandle) => fileHandle <= MaxOpenFiles;

    private DosFileOperationResult NoFreeHandleError() {
        if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
            _loggerService.Warning("Could not find a free handle {MethodName}", nameof(NoFreeHandleError));
        }
        return DosFileOperationResult.Error(ErrorCode.TooManyOpenFiles);
    }

    internal string? TryGetFullHostPathFromDos(string dosPath) => _dosPathResolver.GetFullHostPathFromDosOrDefault(dosPath);

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

    private DosFileOperationResult OpenFileInternal(string dosFileName, string? hostFileName, string openMode) {
        if (string.IsNullOrWhiteSpace(hostFileName)) {
            // Not found
            return FileNotFoundError(dosFileName);
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
                        string? realFileName = _dosPathResolver.GetFullHostPathFromDosOrDefault(dosFileName);
                        if (File.Exists(hostFileName)) {
                            randomAccessFile = File.OpenRead(hostFileName);
                        } else if (File.Exists(realFileName)) {
                            randomAccessFile = File.OpenRead(realFileName);
                        } else {
                            return FileNotFoundError(dosFileName);
                        }

                        break;
                    }
                case "w":
                    randomAccessFile = File.OpenWrite(hostFileName);
                    break;
                case "rw": {
                        string? realFileName = _dosPathResolver.GetFullHostPathFromDosOrDefault(dosFileName);
                        if (File.Exists(hostFileName)) {
                            randomAccessFile = File.Open(hostFileName, FileMode.Open);
                        } else if (File.Exists(realFileName)) {
                            randomAccessFile = File.Open(realFileName, FileMode.Open);
                        } else {
                            return FileNotFoundError(dosFileName);
                        }
                        break;
                    }
            }

            if (randomAccessFile != null) {
                SetOpenFile(dosIndex, new OpenFile(dosFileName, dosIndex, randomAccessFile));
            }
        } catch (FileNotFoundException) {
            return FileNotFoundError(dosFileName);
        } catch (IOException) {
            return FileAccessDeniedError(dosFileName);
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

    private void UpdateDosTransferAreaWithFileMatch(DosDiskTransferArea dta, string matchingFileSystemEntry, ushort? searchAttributes = null) {
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("Found matching file {MatchingFileSystemEntry}", matchingFileSystemEntry);
        }

        FileSystemInfo entryInfo = Directory.Exists(matchingFileSystemEntry) ? new DirectoryInfo(matchingFileSystemEntry) : new FileInfo(matchingFileSystemEntry);
        DateTime creationZonedDateTime = entryInfo.CreationTimeUtc;
        DateTime creationLocalDate = creationZonedDateTime.ToLocalTime();
        DateTime creationLocalTime = creationZonedDateTime.ToLocalTime();
        dta.Drive = DefaultDrive;
        dta.SearchAttributes = searchAttributes ?? dta.SearchAttributes;
        dta.FileAttributes = (byte)entryInfo.Attributes;
        dta.FileDate = ToDosDate(creationLocalDate);
        dta.FileTime = ToDosTime(creationLocalTime);
        if (entryInfo is FileInfo fileInfo) {
            dta.FileSize = (ushort)fileInfo.Length;
        } else {
            // The FAT node entry size for a directory
            dta.FileSize = 4096;
        }
        dta.FileName = GetShortFileName(Path.GetFileName(matchingFileSystemEntry));
    }

    private string GetShortFileName(string longFileName) {
        string filePart = Path.GetFileNameWithoutExtension(longFileName);
        string extPart = Path.GetExtension(longFileName);
        return $"{(filePart.Length > 8 ? filePart[0..7] : filePart)}{(extPart.Length > 4 ? extPart[0..3] : extPart)}";
    }

    private DosDiskTransferArea GetDosDiskTransferArea() {
        uint diskTransferAreaPhysicalAddress = GetDiskTransferAreaPhysicalAddress();
        DosDiskTransferArea dosDiskTransferArea = new(_memory, diskTransferAreaPhysicalAddress);
        return dosDiskTransferArea;
    }

    /// <summary>
    /// Creates a directory on disk.
    /// </summary>
    /// <param name="dosDirectory">The directory name to create</param>
    /// <returns>A <see cref="DosFileOperationResult"/> with details about the result of the operation.</returns>
    public DosFileOperationResult CreateDirectory(string dosDirectory) {
        string? parentFolder = _dosPathResolver.GetFullHostParentPathFromDosOrDefault(dosDirectory);
        if (string.IsNullOrWhiteSpace(parentFolder)) {
            return PathNotFoundError(dosDirectory);
        }

        if (_dosPathResolver.AnyDosDirectoryOrFileWithTheSameName(dosDirectory, new DirectoryInfo(parentFolder))) {
            return FileAccessDeniedError(dosDirectory);
        }

        string prefixedDosDirectory = _dosPathResolver.PrefixWithHostDirectory(dosDirectory);
        try {
            Directory.CreateDirectory(prefixedDosDirectory);
            if (_loggerService.IsEnabled(LogEventLevel.Information)) {
                _loggerService.Information("Created dir: {CreatedDirPath}", prefixedDosDirectory);
            }
            return DosFileOperationResult.NoValue();
        } catch (IOException e) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning(e, "Error while creating directory {CaseInsensitivePath}: {Exception}",
                    prefixedDosDirectory, e);
            }
        }

        return PathNotFoundError(dosDirectory);
    }

    /// <summary>
    /// Removes a file on disk.
    /// </summary>
    /// <param name="dosFile">The file name to delete</param>
    /// <returns>A <see cref="DosFileOperationResult"/> with details about the result of the operation.</returns>
    public DosFileOperationResult RemoveFile(string dosFile) {
        string? fullHostPath = _dosPathResolver.GetFullHostPathFromDosOrDefault(dosFile);
        if (string.IsNullOrWhiteSpace(fullHostPath)) {
            return PathNotFoundError(dosFile);
        }

        try {
            File.Delete(fullHostPath);
            if (_loggerService.IsEnabled(LogEventLevel.Information)) {
                _loggerService.Information("Deleted dir: {DeletedDirPath}", fullHostPath);
            }

            return DosFileOperationResult.NoValue();
        } catch (IOException e) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning(e, "Error while deleting file {CaseInsensitivePath}: {Exception}",
                    fullHostPath, e);
            }
        }

        return PathNotFoundError(dosFile);
    }

    /// <summary>
    /// Removes a directory on disk.
    /// </summary>
    /// <param name="dosDirectory">The directory name to delete</param>
    /// <returns>A <see cref="DosFileOperationResult"/> with details about the result of the operation.</returns>
    public DosFileOperationResult RemoveDirectory(string dosDirectory) {
        string? fullHostPath = _dosPathResolver.GetFullHostPathFromDosOrDefault(dosDirectory);
        if (string.IsNullOrWhiteSpace(fullHostPath)) {
            return PathNotFoundError(dosDirectory);
        }
        bool triedToDeleteCurrentDir;
        try {
            Directory.Delete(fullHostPath);
            if (_loggerService.IsEnabled(LogEventLevel.Information)) {
                _loggerService.Information("Deleted dir: {DeletedDirPath}", fullHostPath);
            }

            return DosFileOperationResult.NoValue();
        } catch (IOException e) {
            triedToDeleteCurrentDir = true;
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning(e, "Error while deleting directory {FullHostPath}: {Exception}",
                    fullHostPath, e);
            }
        }
        return triedToDeleteCurrentDir ? RemoveCurrentDirError(dosDirectory) : PathNotFoundError(dosDirectory);
    }

    /// <summary>
    /// Gets the current DOS directory.
    /// </summary>
    /// <param name="driveNumber">The drive number (0x0: default, 0x1: A:, 0x2: B:, 0x3: C:, ...)</param>
    /// <param name="currentDir">The string variable receiving the current DOS directory.</param>
    /// <returns>A <see cref="DosFileOperationResult"/> with details about the result of the operation.</returns>
    public DosFileOperationResult GetCurrentDir(byte driveNumber, out string currentDir) => _dosPathResolver.GetCurrentDosDirectory(driveNumber, out currentDir);

    /// <summary>
    /// Gets the current default drive. 0x0: A:, 0x1: B:, ...
    /// </summary>
    public byte DefaultDrive => _dosPathResolver.CurrentDriveIndex;

    /// <summary>
    /// Selects the DOS default drive.
    /// </summary>
    /// <param name="driveIndex">The index of the drive. 0x0: A:, 0x1: B:, ...</param>
    public void SelectDefaultDrive(byte driveIndex) => _dosPathResolver.CurrentDriveIndex = driveIndex;

    /// <summary>
    /// Gets the number of potentially valid drive letters
    /// </summary>
    public byte NumberOfPotentiallyValidDriveLetters => _dosPathResolver.NumberOfPotentiallyValidDriveLetters;
}