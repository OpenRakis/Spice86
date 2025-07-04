
namespace Spice86.Core.Emulator.OperatingSystem;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem.Devices;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Emulator.Errors;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;

/// <summary>
/// The class that implements DOS file operations, such as finding files, allocating file handles, and updating the Disk Transfer Area.
/// </summary>
public class DosFileManager {
    private static readonly char[] _directoryChars = {
        DosPathResolver.DirectorySeparatorChar,
        DosPathResolver.AltDirectorySeparatorChar };

    private readonly DosDriveManager _dosDriveManager;

    private readonly ILoggerService _loggerService;

    private ushort _diskTransferAreaAddressOffset;

    private ushort _diskTransferAreaAddressSegment;

    private readonly IMemory _memory;

    private readonly DosPathResolver _dosPathResolver;

    private readonly Dictionary<byte, (string FileSystemEntry, string FileSpec)> _activeFileSearches = new();

    private readonly DosStringDecoder _dosStringDecoder;

    /// <summary>
    /// All the files opened by DOS.
    /// </summary>
    public VirtualFileBase?[] OpenFiles { get; } = new VirtualFileBase[0xFF];

    private readonly IList<IVirtualDevice> _dosVirtualDevices;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="dosStringDecoder">A helper class to encode/decode DOS strings.</param>
    /// <param name="dosDriveManager">The class used to manage folders mounted as DOS drives.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    /// <param name="dosVirtualDevices">The virtual devices from the DOS kernel.</param>
    public DosFileManager(IMemory memory, DosStringDecoder dosStringDecoder,
        DosDriveManager dosDriveManager, ILoggerService loggerService, IList<IVirtualDevice> dosVirtualDevices) {
        _loggerService = loggerService;
        _dosStringDecoder = dosStringDecoder;
        _dosPathResolver = new(dosDriveManager);
        _memory = memory;
        _dosDriveManager = dosDriveManager;
        _dosVirtualDevices = dosVirtualDevices;
    }

    /// <summary>
    /// Gets the standard input, which is the first open character device with the <see cref="DeviceAttributes.CurrentStdin"/> attribute.
    /// </summary>
    /// <returns>The standard input device, or <c>null</c> if not found.</returns>
    public bool TryGetStandardInput([NotNullWhen(true)] out CharacterDevice? device) {
        bool result = TryGetOpenDeviceWithAttributes<CharacterDevice>(DeviceAttributes.CurrentStdin, out device);
        return result;
    }

    /// <summary>
    /// Gets the standard output, which is the first open character device with the <see cref="DeviceAttributes.CurrentStdout"/> attribute.
    /// </summary>
    /// <returns>The standard output device, or <c>null</c> if not found.</returns>
    public bool TryGetStandardOutput([NotNullWhen(true)] out CharacterDevice? device) {
        bool result = TryGetOpenDeviceWithAttributes<CharacterDevice>(DeviceAttributes.CurrentStdout, out device);
        return result;
    }

    /// <summary>
    /// Gets the device file handle of the first open character device with the specified attributes.
    /// </summary>
    /// <param name="attributes">The device attributes, such as <see cref="DeviceAttributes.CurrentStdin"/>. <br/>
    /// There can be several.</param>
    /// <param name="device">The DOS device if found, or <c>null</c> if not found.</param>
    /// <returns>Whether the DOS Device was found.</returns>
    public bool TryGetOpenDeviceWithAttributes<T>(DeviceAttributes attributes,
        [NotNullWhen(true)] out T? device) where T : IVirtualDevice {
        device = OpenFiles.OfType<T>().FirstOrDefault(x => x is
            CharacterDevice device &&
            device.Header.Attributes.HasFlag(attributes));
        return device is not null;
    }

    /// <summary>
    /// Closes a file handle.
    /// </summary>
    /// <param name="fileHandle">The file handle to an open file.</param>
    /// <returns>A <see cref="DosFileOperationResult"/> with details about the result of the operation.</returns>
    /// <exception cref="UnrecoverableException">If the host OS refuses to close the file.</exception>
    public DosFileOperationResult CloseFile(ushort fileHandle) {
        if (GetOpenFile(fileHandle) is not DosFile file) {
            return FileNotOpenedError(fileHandle);
        }

        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("Closed {ClosedFileName}, file was loaded in ram in those addresses: {ClosedFileAddresses}", file.Name, file.LoadedMemoryRanges);
        }

        SetOpenFile(fileHandle, null);
        try {
            if (CountHandles(file) == 0) {
                // Only close the file if no other handle to it exist.
                file.Close();
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

        return OpenFileInternal(fileName, prefixedPath, FileAccessMode.ReadWrite);
    }

    /// <summary>
    /// Gets another file handle for the same file
    /// </summary>
    /// <param name="fileHandle">The handle to a file.</param>
    /// <param name="newHandle">The new handle to a file.</param>
    /// <returns>A <see cref="DosFileOperationResult"/> with details about the result of the operation.</returns>
    public DosFileOperationResult ForceDuplicateFileHandle(ushort fileHandle, ushort newHandle) {
        if (fileHandle == newHandle) {
            return DosFileOperationResult.Error(ErrorCode.InvalidHandle);
        }
        // Out of range in our array
        if (!IsHandleInRange(newHandle)) {
            return DosFileOperationResult.Error(ErrorCode.InvalidHandle);
        }
        // New handle is already opened
        if (OpenFiles[newHandle] != null) {
            return DosFileOperationResult.Error(ErrorCode.InvalidHandle);
        }
        // Open it and assign it to a var named file.
        // Should always pass: we already called IsValidFileHandle for our own error code.
        if (GetOpenFile(fileHandle) is not VirtualFileBase file) {
            return FileNotOpenedError(fileHandle);
        }
        if (newHandle < OpenFiles.Length && OpenFiles[newHandle] != null) {
            CloseFile(newHandle);
        }
        SetOpenFile(newHandle, file);
        return DosFileOperationResult.NoValue();
    }

    /// <summary>
    /// Gets another file handle for the same file
    /// </summary>
    /// <param name="fileHandle">The handle to a file.</param>
    /// <returns>A <see cref="DosFileOperationResult"/> with details about the result of the operation.</returns>
    public DosFileOperationResult DuplicateFileHandle(ushort fileHandle) {
        if (GetOpenFile(fileHandle) is not VirtualFileBase file) {
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
        dta.Drive = _dosDriveManager.CurrentDriveIndex;
        dta.EntryCountWithinSearchResults = 0;

        if (_dosVirtualDevices.OfType<CharacterDevice>().SingleOrDefault(
            x => x.IsName(fileSpec)) is { } characterDevice) {
            if (!TryUpdateDosTransferAreaWithFileMatch(dta,
                characterDevice.Name,
                out DosFileOperationResult status, searchAttributes)) {
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
            string? searchPattern = GetFileSpecWithoutSubFolderOrDriveInIt(fileSpec) ?? fileSpec;
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
        if (index == -1) {
            index = fileSpec.LastIndexOfAny([DosPathResolver.VolumeSeparatorChar]);
        }
        if (index != -1) {
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
        if (searchDrive >= 26 || searchDrive > _dosDriveManager.NumberOfPotentiallyValidDriveLetters) {
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

    private bool TryUpdateDosTransferAreaWithFileMatch(DosDiskTransferArea dta, string filename, out DosFileOperationResult status, ushort? searchAttributes = null) {
        try {
            UpdateDosTransferAreaWithFileMatch(dta, filename, searchAttributes);
        } catch (IOException e) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
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
    public DosFileOperationResult MoveFilePointerUsingHandle(SeekOrigin originOfMove, ushort fileHandle, uint offset) {
        if (GetOpenFile(fileHandle) is not VirtualFileBase file) {
            return FileNotOpenedError(fileHandle);
        }

        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("Moving in file {FileMove}", file.Name);
        }
        try {
            long newOffset = file.Seek(offset, originOfMove);
            return DosFileOperationResult.Value32((uint)newOffset);
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
    public DosFileOperationResult OpenFile(string fileName, FileAccessMode accessMode) {
        CharacterDevice? device = _dosVirtualDevices.OfType<CharacterDevice>()
            .FirstOrDefault(device => device.IsName(fileName));
        if (device is not null) {
            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                _loggerService.Verbose("Opening device {FileName} with mode {OpenMode}", fileName, accessMode);
            }
            return OpenDevice(device);
        }

        string? hostFileName = _dosPathResolver.GetFullHostPathFromDosOrDefault(fileName);
        if (string.IsNullOrWhiteSpace(hostFileName)) {
            if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                _loggerService.Error("DOS: File not found! {DosFilePathNotFound} {AccessMode}", fileName, accessMode);
            }
            return FileNotFoundError($"'{fileName}'");
        }

        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("Opening file {HostFileName} with mode {OpenMode}", hostFileName, accessMode);
        }

        return OpenFileInternal(fileName, hostFileName, accessMode);
    }

    /// <summary>
    /// Returns a handle to a DOS <see cref="CharacterDevice"/>
    /// </summary>
    /// <param name="device">The character device</param>
    /// <returns>A <see cref="DosFileOperationResult"/> with details about the result of the operation.</returns>
    public DosFileOperationResult OpenDevice(VirtualFileBase device) {
        int? freeIndex = FindNextFreeFileIndex();
        if (freeIndex == null) {
            return NoFreeHandleError();
        }

        ushort dosIndex = (ushort)freeIndex.Value;
        SetOpenFile(dosIndex, device);

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
        if (GetOpenFile(fileHandle) is not VirtualFileBase file) {
            return FileNotOpenedError(fileHandle);
        }

        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("Reading from file {FileName}", file.Name);
        }

        byte[] buffer = new byte[readLength];
        int actualReadLength;
        try {
            actualReadLength = file.Read(buffer, 0, readLength);
        } catch (IOException e) {
            throw new UnrecoverableException("IOException while reading file", e);
        }

        if (actualReadLength < 1) {
            // EOF
            return DosFileOperationResult.Value16(0);
        }

        if (actualReadLength > 0) {
            _memory.LoadData(targetAddress, buffer, actualReadLength);
            if (file is DosFile actualFile) {
                actualFile.AddMemoryRange(new MemoryRange(targetAddress,
                    (uint)(targetAddress + actualReadLength - 1), file.Name));
            }
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
    public DosFileOperationResult WriteToFileOrDevice(ushort fileHandle, ushort writeLength, uint bufferAddress) {
        if (!IsHandleInRange(fileHandle)) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("Invalid or unsupported file handle {FileHandle}. Doing nothing", fileHandle);
            }
        }

        VirtualFileBase? file = GetOpenFile(fileHandle);
        if (file == null) {
            return FileNotOpenedError(fileHandle);
        }

        try {
            // Do not access Ram property directly to trigger breakpoints if needed
            Span<byte> data = _memory.GetSpan((int)bufferAddress, writeLength);

            string valueAsString = _dosStringDecoder.ConvertDosChars(data);
            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                _loggerService.Verbose("Writing to file or device content: {Name} {Bytes} {CodePage850String}",
                    file.Name, data.ToArray(), valueAsString);
            }

            file.Write(data);
        } catch (IOException e) {
            throw new UnrecoverableException("IOException while writing file", e);
        }

        return DosFileOperationResult.Value16(writeLength);
    }

    private int CountHandles(DosFile openFileToCount) => OpenFiles.Count(openFile => openFile == openFileToCount);

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

    private VirtualFileBase? GetOpenFile(ushort fileHandle) {
        if (!IsHandleInRange(fileHandle)) {
            return null;
        }
        return OpenFiles[fileHandle];
    }

    private bool IsHandleInRange(ushort fileHandle) => fileHandle <= OpenFiles.Length;

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

    private DosFileOperationResult OpenFileInternal(string dosFileName, string? hostFileName, FileAccessMode openMode) {
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
                case FileAccessMode.ReadOnly: {
                        string? realFileName = _dosPathResolver.GetFullHostPathFromDosOrDefault(dosFileName);
                        if (File.Exists(hostFileName)) {
                            randomAccessFile = File.Open(hostFileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        } else if (File.Exists(realFileName)) {
                            randomAccessFile = File.Open(realFileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        } else {
                            return FileNotFoundError(dosFileName);
                        }

                        break;
                    }
                case FileAccessMode.WriteOnly:
                    randomAccessFile = File.Open(hostFileName, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
                    break;
                case FileAccessMode.ReadWrite: {
                        string? realFileName = _dosPathResolver.GetFullHostPathFromDosOrDefault(dosFileName);
                        if (File.Exists(hostFileName)) {
                            randomAccessFile = File.Open(hostFileName, FileMode.Open ,FileAccess.ReadWrite, FileShare.ReadWrite);
                        } else if (File.Exists(realFileName)) {
                            randomAccessFile = File.Open(realFileName, FileMode.Open ,FileAccess.ReadWrite, FileShare.ReadWrite);
                        } else {
                            return FileNotFoundError(dosFileName);
                        }
                        break;
                    }
            }

            if (randomAccessFile != null) {
                SetOpenFile(dosIndex, new DosFile(dosFileName, dosIndex, randomAccessFile) {
                    Drive = _dosDriveManager.CurrentDriveIndex
                });
            }
        } catch (FileNotFoundException) {
            return FileNotFoundError(dosFileName);
        } catch (IOException) {
            return FileAccessDeniedError(dosFileName);
        }

        return DosFileOperationResult.Value16(dosIndex);
    }

    private void SetOpenFile(ushort fileHandle, VirtualFileBase? openFile) => OpenFiles[fileHandle] = openFile;

    private void UpdateDosTransferAreaWithFileMatch(DosDiskTransferArea dta,
        string matchingFileSystemEntry, ushort? searchAttributes = null) {
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("Found matching file {MatchingFileSystemEntry}", matchingFileSystemEntry);
        }

        FileSystemInfo entryInfo = Directory.Exists(matchingFileSystemEntry) ?
            new DirectoryInfo(matchingFileSystemEntry) : new FileInfo(matchingFileSystemEntry);
        DateTime creationZonedDateTime = entryInfo.CreationTimeUtc;
        DateTime creationLocalDate = creationZonedDateTime.ToLocalTime();
        DateTime creationLocalTime = creationZonedDateTime.ToLocalTime();
        dta.Drive = _dosDriveManager.CurrentDriveIndex;
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
        string? hostDirToDelete = _dosPathResolver.GetFullHostPathFromDosOrDefault(dosDirectory);

        if (string.IsNullOrWhiteSpace(hostDirToDelete) ||
            Directory.Exists(hostDirToDelete)) {
            return PathNotFoundError(dosDirectory);
        }

        _dosPathResolver.GetCurrentDosDirectory(_dosDriveManager.CurrentDriveIndex, out string currentDir);
        string? currentHostPath = _dosPathResolver.GetFullHostPathFromDosOrDefault(currentDir);

        if (!string.IsNullOrWhiteSpace(currentHostPath) &&
            currentHostPath.StartsWith(hostDirToDelete, StringComparison.OrdinalIgnoreCase)) {
            return RemoveCurrentDirError(dosDirectory);
        }

        try {
            Directory.Delete(hostDirToDelete);
            if (_loggerService.IsEnabled(LogEventLevel.Information)) {
                _loggerService.Information("Deleted dir: {DeletedDirPath}", hostDirToDelete);
            }

            return DosFileOperationResult.NoValue();
        } catch (IOException e) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning(e, "Error while deleting directory {CaseInsensitivePath}: {Exception}",
                    hostDirToDelete, e);
            }
        }

        return PathNotFoundError(dosDirectory);
    }

    /// <summary>
    /// Gets the current DOS directory.
    /// </summary>
    /// <param name="driveNumber">The drive number (0x0: default, 0x1: A:, 0x2: B:, 0x3: C:, ...)</param>
    /// <param name="currentDir">The string variable receiving the current DOS directory.</param>
    /// <returns>A <see cref="DosFileOperationResult"/> with details about the result of the operation.</returns>
    public DosFileOperationResult GetCurrentDir(byte driveNumber, out string currentDir) =>
        _dosPathResolver.GetCurrentDosDirectory(driveNumber, out currentDir);

    public DosFileOperationResult IoControl(State state) {
        byte handle = 0;
        byte drive = 0;

        if (state.AL is < 4 or 0x06 or 0x07 or
            0x0a or 0x0c or 0x10) {
            handle = (byte)state.BX;
            if (handle >= OpenFiles.Length ||
                OpenFiles[handle] == null) {
                return DosFileOperationResult.Error(ErrorCode.InvalidHandle);
            }
        } else if (state.AL < 0x12) {
            if (state.AL != 0x0b) {
                drive = (byte)(state.BX == 0 ? _dosDriveManager.CurrentDriveIndex : state.BX - 1);
                if (drive >= 2 && (drive >= _dosDriveManager.NumberOfPotentiallyValidDriveLetters ||
                    _dosDriveManager.Count < (drive + 1))) {
                    return DosFileOperationResult.Error(ErrorCode.InvalidDrive);
                }
            }
        } else {
            return DosFileOperationResult.Error(ErrorCode.FunctionNumberInvalid);
        }

        switch (state.AL) {
            case 0x00:      /* Get Device Information */
                VirtualFileBase? fileOrDevice = OpenFiles[handle];
                if (fileOrDevice is VirtualDeviceBase virtualDevice) {
                    state.DX = virtualDevice.Information;
                } else if (fileOrDevice is DosFile dosFile) {
                    byte sourceDrive = dosFile.Drive;
                    if (sourceDrive == 0xff) {
                        if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                            _loggerService.Warning("No drive set for file handle {FileHandle}", handle);
                        }
                        sourceDrive = 0x2; // defaulting to C:
                    }
                    ushort dosFileInfo = (ushort)(0xffe0 | sourceDrive);
                    state.DX = dosFileInfo;
                    return DosFileOperationResult.Value16(dosFileInfo);
                }
                return DosFileOperationResult.Value16(state.DX);
            case 0x01:      /* Set Device Information */
                if (state.DH != 0) {
                    return DosFileOperationResult.Error(ErrorCode.DataInvalid);
                }
                if (OpenFiles[handle] is IVirtualDevice device && (device.Information & 0x8000) > 9) {
                    state.AL = device.GetStatus(true);
                } else {
                    return DosFileOperationResult.Error(ErrorCode.FunctionNumberInvalid);
                }
                return DosFileOperationResult.NoValue();
            case 0x02:      /* Read from Device Control Channel */
                if (OpenFiles[handle] is IVirtualDevice readDevice &&
                    (readDevice.Information & 0xc000) > 0) {
                    if (readDevice is PrinterDevice printer && !printer.CanRead) {
                        return DosFileOperationResult.Error(ErrorCode.AccessDenied);
                    }
                    uint buffer = MemoryUtils.ToPhysicalAddress(state.DS, state.DX);
                    if (readDevice.TryReadFromControlChannel(buffer, state.CX, out ushort? returnCode)) {
                        state.AX = returnCode.Value;
                        return DosFileOperationResult.NoValue();
                    }
                }
                return DosFileOperationResult.Error(ErrorCode.FunctionNumberInvalid);
            case 0x03:      /* Write to Device Control Channel */
                if (OpenFiles[handle] is IVirtualDevice writtenDevice &&
                    (writtenDevice.Information & 0xc000) > 0) {
                    /* is character device with IOCTL support */
                    if (writtenDevice is PrinterDevice printer && !printer.CanWrite) {
                        return DosFileOperationResult.Error(ErrorCode.AccessDenied);
                    }
                    uint buffer = MemoryUtils.ToPhysicalAddress(state.DS, state.DX);
                    if (writtenDevice.TryWriteToControlChannel(buffer, state.CX, out ushort? returnCode)) {
                        state.AX = returnCode.Value;
                        return DosFileOperationResult.NoValue();
                    }
                }
                return DosFileOperationResult.Error(ErrorCode.FunctionNumberInvalid);
            case 0x06:      /* Get Input Status */
                if (OpenFiles[handle] is IVirtualDevice inputDevice) {
                    if ((inputDevice.Information & 0x8000) > 0) {
                        if (((inputDevice.Information & 0x40) > 0)) {
                            state.AL = 0;
                        } else {
                            state.AL = 0xFF;
                        }
                    }
                } else if (OpenFiles[handle] is VirtualFileBase file) {
                    long oldLocation = file.Position;
                    file.Seek(file.Position, SeekOrigin.End);
                    long endLocation = file.Position;
                    if (oldLocation < endLocation) { //Still data available
                        state.AL = 0xff;
                    } else {
                        state.AL = 0x0; //EOF or beyond
                    }
                    file.Seek(oldLocation, SeekOrigin.Begin); //restore filelocation
                    if (_loggerService.IsEnabled(LogEventLevel.Information)) {
                        _loggerService.Information(
                            "DOS IOCTL: Get input status: Operation done one regular file with handle {Handle}",
                            handle);
                    }
                }
                return DosFileOperationResult.NoValue();
            case 0x07:      /* Get Output Status */
                if (OpenFiles[handle] is IVirtualDevice outputDevice &&
                    (outputDevice.Information & 0x0200) > 0) {
                    state.AL = outputDevice.GetStatus(false);
                }
                if (_loggerService.IsEnabled(LogEventLevel.Information)) {
                    _loggerService.Information(
                        "DOS IOCTL: Get output status: Fakes output status is ready for handle {Handle}",
                        handle);
                }
                state.AL = 0xFF;
                return DosFileOperationResult.NoValue();

            case 0x08:      /* Check if block device removable */
                //* cdrom drives and drive A and B are removable */
                if (drive < 2) {
                    state.AX = 0;
                } else if (!_dosDriveManager.ElementAtOrDefault(drive).Value.IsRemovable) {
                    state.AX = 1;
                } else {
                    return DosFileOperationResult.Error(ErrorCode.FunctionNumberInvalid);
                }
                return DosFileOperationResult.NoValue();
            case 0x09:      /* Check if block device remote */
                if ((drive >= 2) && _dosDriveManager.ElementAt(drive).Value.IsRemote) {
                    state.DX = 0x1000;  // device is remote
                                        // undocumented bits always clear
                } else {
                    state.DX = 0x0802;  // Open/Close supported; 32bit access supported (any use? fixes Fable installer)
                                        // undocumented bits from device attribute word
                                        // TODO Set bit 9 on drives that don't support direct I/O
                }
                return DosFileOperationResult.NoValue();
            case 0x0B:      /* Set sharing retry count */
                if (state.DX == 0) {
                    return DosFileOperationResult.Error(ErrorCode.FunctionNumberInvalid);
                }
                return DosFileOperationResult.NoValue();
            case 0x0D:      /* Generic block device request */
                if (drive < 2 && _dosDriveManager.ElementAtOrDefault(drive).Value is null) {
                    return DosFileOperationResult.Error(ErrorCode.AccessDenied);
                }
                if (state.CH != 0x08 || _dosDriveManager.ElementAtOrDefault(drive).Value.IsRemovable) {
                    return DosFileOperationResult.Error(ErrorCode.FunctionNumberInvalid);
                }
                uint ptr = MemoryUtils.ToPhysicalAddress(state.DS, state.DX);

                switch (state.CL) {
                    case 0x60:  // Get Device Parameters
                        _memory.UInt8[ptr + 1] = (byte)(drive >= 2 ? 0x05 : 0x07);  // type
                        _memory.UInt16[ptr + 2] = (ushort)(drive >= 2 ? 0x01 : 0x00); // attributes
                        _memory.UInt16[ptr + 4] = 0x0000;                            // cylinders
                        _memory.UInt8[ptr + 6] = 0x00;                              // media type
                        _memory.UInt16[ptr + 7] = 0x0200;                            // bytes per sector
                        break;

                    case 0x46:  // Set Volume Serial Number (not yet implemented)
                                // TODO: pull new serial from DS:DX buffer and store it somewhere
                        break;

                    case 0x66:  // Get Volume Serial Number + Volume Label + FS Type
                    {
                            // 1) Build the 11-byte volume label (padded with spaces)
                            // 1) pull the raw label (or default), split name/ext
                            VirtualDrive driveInfo = _dosDriveManager.ElementAtOrDefault(drive).Value;
                            string rawLabel = driveInfo.Label.ToUpperInvariant();
                            string[] parts = rawLabel.Split(['.'], 2);
                            string name = parts[0];
                            string ext = parts.Length > 1 ? parts[1] : "";

                            // 2) build the 11-byte volume label
                            var sbLabel = new StringBuilder(11);
                            sbLabel.Append(name.Length > 8 ? name[..8] : name);
                            sbLabel.Append(ext.Length > 3 ? ext[..3] : ext);
                            if (sbLabel.Length < 11)
                                sbLabel.Append(' ', 11 - sbLabel.Length);

                            // 3) build the 8-byte FS ID (FAT16 or FAT12 for floppies)
                            var sbFs = new StringBuilder(8);
                            sbFs.Append(drive < 2 ? "FAT12" : "FAT16");
                            if (sbFs.Length < 8)
                                sbFs.Append(' ', 8 - sbFs.Length);

                            // 4) write serial, label, fs-id into emulated memory
                            _memory.UInt32[ptr + 2] = 0x1234; // serial
                            _memory.SetZeroTerminatedString(ptr + 6, sbLabel.ToString(), sbLabel.Length + 1);
                            _memory.SetZeroTerminatedString(ptr + 0x11, sbFs.ToString(), sbFs.Length + 1);
                            break;
                        }

                    default:
                        _loggerService.Error(
                          "DOS IOCTL Call 0D:{0:X2} Drive {1:X2} unhandled", state.CL, drive);
                        return DosFileOperationResult.Error(ErrorCode.FunctionNumberInvalid);
                }
                state.AX = 0;
                return DosFileOperationResult.NoValue();
            case 0x0E:          /* Get Logical Drive Map */
                if (drive < 2) {
                    if (_dosDriveManager.HasDriveAtIndex(drive)) {
                        state.AL = (byte)(drive + 1);
                    } else {
                        state.AL = 1;
                    }
                } else if (_dosDriveManager.ElementAtOrDefault(drive).Value.IsRemovable) {
                    return DosFileOperationResult.Error(ErrorCode.FunctionNumberInvalid);
                } else { /* Only 1 logical drive assigned */
                    state.AL = 0;
                    state.AH = 0x07;
                }
                return DosFileOperationResult.NoValue();
            default:
                if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                    _loggerService.Warning("IOCTL: Invalid function number {IoctlFunc}", state.AL);
                }
                return DosFileOperationResult.Error(ErrorCode.FunctionNumberInvalid);
        }
    }
}