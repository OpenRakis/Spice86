
namespace Spice86.Core.Emulator.OperatingSystem;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem.Devices;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Emulator.Errors;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;

/// <summary>
/// The class that implements DOS file operations, such as finding files,
/// allocating file handles, and updating the Disk Transfer Area.
/// </summary>
public class DosFileManager {
    private const int ExtDeviceBit = 0x0200;
    private static readonly char[] _directoryChars = {
        DosPathResolver.DirectorySeparatorChar,
        DosPathResolver.AltDirectorySeparatorChar };

    private readonly DosDriveManager _dosDriveManager;

    private readonly ILoggerService _loggerService;

    private ushort _diskTransferAreaAddressOffset;

    private ushort _diskTransferAreaAddressSegment;

    private readonly IMemory _memory;

    private readonly DosPathResolver _dosPathResolver;

    private class FileSearchPrivateData {
        public FileSearchPrivateData(string fileSpec, int index, ushort searchAttributes) {
            FileSpec = fileSpec;
            Index = index;
            SearchAttributes = searchAttributes;
        }

        public string FileSpec { get; set; }

        public int Index { get; set; }

        public ushort SearchAttributes { get; init; }
    }

    private readonly Dictionary<uint, FileSearchPrivateData> _activeFileSearches = new();

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
        _dosPathResolver = new DosPathResolver(dosDriveManager);
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
    public DosFileOperationResult CloseFileOrDevice(ushort fileHandle) {
        if (GetOpenFile(fileHandle) is not DosFile file) {
            return FileNotOpenedError(fileHandle);
        }

        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("Closed {ClosedFileName}, file was loaded in ram in those addresses: {ClosedFileAddresses}",
                file.Name, file.LoadedMemoryRanges);
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
    /// Closes all non-standard file handles (handles 5 and above) when a process terminates.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Standard file handles (0-4) are:
    /// - 0: stdin
    /// - 1: stdout
    /// - 2: stderr
    /// - 3: stdaux (auxiliary device)
    /// - 4: stdprn (printer)
    /// </para>
    /// <para>
    /// These are inherited from the parent and should not be closed when a child terminates.
    /// Only handles 5 and above (user-opened files) are closed.
    /// </para>
    /// </remarks>
    public void CloseAllNonStandardFileHandles() {
        // Standard handles 0-4 are stdin, stdout, stderr, stdaux, stdprn
        // These should not be closed when a process terminates
        const ushort firstUserHandle = 5;
        
        for (ushort handle = firstUserHandle; handle < OpenFiles.Length; handle++) {
            if (OpenFiles[handle] is DosFile) {
                if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                    _loggerService.Debug("Closing file handle {Handle} on process termination", handle);
                }
                CloseFileOrDevice(handle);
            }
        }
    }

    /// <summary>
    /// Creates a file and returns the handle to the file.
    /// </summary>
    /// <param name="fileName">The target file name.</param>
    /// <param name="fileAttribute">The file system attributes to set on the new file.</param>
    /// <returns>A <see cref="DosFileOperationResult"/> with details about the result of the operation.</returns>
    /// <exception cref="UnrecoverableException"></exception>
    public DosFileOperationResult CreateFileUsingHandle(string fileName, ushort fileAttribute) {
        CharacterDevice? device = _dosVirtualDevices.OfType<CharacterDevice>()
            .FirstOrDefault(device => device.IsName(fileName));
        if (device is not null) {
            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                _loggerService.Verbose("Opening device {FileName}", fileName);
            }
            return OpenDevice(device);
        }

        string newHostFilePath = _dosPathResolver.PrefixWithHostDirectory(fileName);

        FileStream? testFileStream = null;
        try {
            if (_loggerService.IsEnabled(LogEventLevel.Information)) {
                _loggerService.Information("Creating file using handle: {PrefixedPath} with {Attributes}",
                    newHostFilePath, fileAttribute);
            }

            // PYRO2 opens a file and re-creates it again.
            // Ensure we close it if it was opened first,
            // in order to avoid an exception
            for (ushort i = 0; i < OpenFiles.Length; i++) {
                VirtualFileBase? virtualFile = OpenFiles[i];
                if (virtualFile is DosFile dosFile) {
                    string? openHostFilePath = _dosPathResolver.GetFullHostPathFromDosOrDefault(dosFile.Name);
                    if (string.Equals(openHostFilePath, newHostFilePath, StringComparison.OrdinalIgnoreCase)) {
                        CloseFileOrDevice(i);
                    }
                }
            }

            testFileStream = File.Create(newHostFilePath);
            File.SetAttributes(newHostFilePath, (FileAttributes)fileAttribute);
        } catch (IOException e) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning(e, "Error while creating a file using a handle with {FileName} and {FileAttribute}",
                    fileName, fileAttribute);
            }
            return PathNotFoundError(fileName);
        } finally {
            testFileStream?.Dispose();
        }

        return OpenFileInternal(fileName, newHostFilePath, FileAccessMode.ReadWrite);
    }

    /// <summary>
    /// Gets another file handle for the same file
    /// </summary>
    /// <param name="fileHandle">The handle to a file.</param>
    /// <param name="newHandle">The new handle to a file.</param>
    /// <returns>A <see cref="DosFileOperationResult"/> with details about the result of the operation.</returns>
    public DosFileOperationResult ForceDuplicateFileHandle(ushort fileHandle, ushort newHandle) {
        if (fileHandle == newHandle) {
            return DosFileOperationResult.Error(DosErrorCode.InvalidHandle);
        }
        // Out of range in our array
        if (!IsHandleInRange(newHandle)) {
            return DosFileOperationResult.Error(DosErrorCode.InvalidHandle);
        }
        // New handle is already opened
        if (OpenFiles[newHandle] != null) {
            return DosFileOperationResult.Error(DosErrorCode.InvalidHandle);
        }
        // Open it and assign it to a var named file.
        // Should always pass: we already called IsValidFileHandle for our own error code.
        if (GetOpenFile(fileHandle) is not VirtualFileBase file) {
            return FileNotOpenedError(fileHandle);
        }
        if (newHandle < OpenFiles.Length && OpenFiles[newHandle] != null) {
            CloseFileOrDevice(newHandle);
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
    /// <param name="fileSpec">a filename with ? when any character can match or * when multiple characters can match.
    /// Case is insensitive</param>
    /// <param name="searchAttributes">The MS-DOS file attributes, such as Directory.</param>
    /// <returns>A <see cref="DosFileOperationResult"/> with details about the result of the operation.</returns>
    public DosFileOperationResult FindFirstMatchingFile(string fileSpec, ushort searchAttributes) {
        if (string.IsNullOrWhiteSpace(fileSpec)) {
            return PathNotFoundError(fileSpec);
        }

        DosDiskTransferArea dta = GetDosDiskTransferArea();
        dta.SearchId = GenerateNewKey();

        if (_dosVirtualDevices.OfType<CharacterDevice>().SingleOrDefault(
            x => x.IsName(fileSpec)) is { } characterDevice) {
            if (!TryUpdateDosTransferAreaWithFileMatch(dta, fileSpec,
                characterDevice.Name, string.Empty,
                out DosFileOperationResult status)) {
                return status;
            }
            _activeFileSearches.Add(dta.SearchId, new
                (characterDevice.Name, 0, searchAttributes));
            return DosFileOperationResult.NoValue();
        }

        if (IsOnlyADosDriveRoot(fileSpec)) {
            return DosFileOperationResult.Error(DosErrorCode.NoMoreFiles);
        }

        if (!GetSearchFolder(fileSpec, out string? searchFolder)) {
            return DosFileOperationResult.Error(DosErrorCode.PathNotFound);
        }

        EnumerationOptions enumerationOptions = GetEnumerationOptions(searchAttributes);

        try {
            string searchPattern = GetFileSpecWithoutSubFolderOrDriveInIt(fileSpec) ?? fileSpec;
            string[] matchingPaths =
                _dosPathResolver.FindFilesUsingWildCmp(searchFolder, searchPattern, enumerationOptions).ToArray();

            if (matchingPaths.Length == 0) {
                return DosFileOperationResult.Error(DosErrorCode.NoMoreFiles);
            }

            if (!TryUpdateDosTransferAreaWithFileMatch(dta, fileSpec, matchingPaths[0], searchFolder,
                out DosFileOperationResult status)) {
                return status;
            }

            _activeFileSearches.Add(dta.SearchId, new(fileSpec, 0, searchAttributes));
            return DosFileOperationResult.NoValue();

        } catch (Exception e) when (e is UnauthorizedAccessException or
            IOException or PathTooLongException or DirectoryNotFoundException
            or ArgumentException) {
            if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                _loggerService.Error(e, "Error while walking path {SearchFolder} or getting attributes",
                    searchFolder);
            }
        }
        return DosFileOperationResult.Error(DosErrorCode.PathNotFound);
    }

    private uint GenerateNewKey() {
        if (_activeFileSearches.Count == 0) {
            return 0;
        }

        return (uint)_activeFileSearches.Keys.Count;
    }

    private bool GetSearchFolder(string fileSpec, [NotNullWhen(true)] out string? searchFolder) {
        string subFolderName = GetSubFoldersInFileSpec(fileSpec) ?? ".";
        searchFolder = _dosPathResolver.GetFullHostPathFromDosOrDefault(subFolderName);
        return searchFolder is not null;
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

    private void UpdateActiveSearch(uint key, string fileSpec) {
        if (_activeFileSearches.TryGetValue(key, out FileSearchPrivateData? search)) {
            search.FileSpec = fileSpec;
            _activeFileSearches[key] = search;
        }
    }

    private static bool IsOnlyADosDriveRoot(string filePath) =>
        filePath.Length == 3 && (filePath[2] == DosPathResolver.DirectorySeparatorChar ||
        filePath[2] == DosPathResolver.AltDirectorySeparatorChar);

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
        uint key = dta.SearchId;
        if (!_activeFileSearches.TryGetValue(key, out FileSearchPrivateData? search)) {
            return FileOperationErrorWithLog($"Call FindFirst first to initiate a search.",
                DosErrorCode.NoMoreFiles);
        }

        if (!GetSearchFolder(search.FileSpec, out string? searchFolder)) {
            return FileOperationErrorWithLog("Search in an invalid folder",
                DosErrorCode.NoMoreFiles);
        }

        string searchPattern = GetFileSpecWithoutSubFolderOrDriveInIt(search.FileSpec) ?? search.FileSpec;
        EnumerationOptions enumerationOptions = GetEnumerationOptions(search.SearchAttributes);
        string[] matchingFiles =
            _dosPathResolver.FindFilesUsingWildCmp(searchFolder, searchPattern, enumerationOptions).ToArray();

        string? fileMatch = matchingFiles.ElementAtOrDefault(search.Index);
        if (matchingFiles.Length == 0 || fileMatch is null) {
            return FileOperationErrorWithLog($"No more files matching for {search.FileSpec} in path {searchFolder}",
                DosErrorCode.NoMoreFiles);
        }

        search.Index++;

        if (!TryUpdateDosTransferAreaWithFileMatch(dta, search.FileSpec,
            fileMatch, searchFolder, out _)) {
            return FileOperationErrorWithLog("Error when getting file system entry attributes of FindNext match.",
                DosErrorCode.NoMoreFiles);
        }
        UpdateActiveSearch(key, search.FileSpec);

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
    public DosFileOperationResult MoveFilePointerUsingHandle(SeekOrigin originOfMove, ushort fileHandle, int offset) {
        if (GetOpenFile(fileHandle) is not VirtualFileBase file) {
            return FileNotOpenedError(fileHandle);
        }

        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("Moving in file {FileMove}", file.Name);
        }
        try {
            long currentPosition = file.Position;
            if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                _loggerService.Debug("DOS Seek handle {Handle} {Origin} offset=0x{Offset:X8} before=0x{Before:X8}",
                    fileHandle, originOfMove, offset, currentPosition);
            }
            long newOffset = file.Seek(offset, originOfMove);
            _loggerService.Debug("DOS Seek handle {Handle} after=0x{After:X8}", fileHandle, newOffset);
            return DosFileOperationResult.Value32((uint)newOffset);
        } catch (IOException e) {
            if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                _loggerService.Error(e, "An error occurred while seeking file {Error}", e);
            }
            return DosFileOperationResult.Error(DosErrorCode.InvalidHandle);
        }
    }

    /// <summary>
    /// Opens a file and returns the file handle.
    /// </summary>
    /// <param name="fileName">The name of the file to open.</param>
    /// <param name="accessMode">The access mode (read, write, or read+write)</param>
    /// <param name="noInherit">If true, the file handle will not be inherited by child processes.</param>
    /// <returns>A <see cref="DosFileOperationResult"/> with details about the result of the operation.</returns>
    public DosFileOperationResult OpenFileOrDevice(string fileName, FileAccessMode accessMode, bool noInherit = false) {
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
            _loggerService.Debug("Opening file {HostFileName} with mode {OpenMode}, noInherit={NoInherit}", hostFileName, accessMode, noInherit);
        }

        return OpenFileInternal(fileName, hostFileName, accessMode, noInherit);
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
    public DosFileOperationResult ReadFileOrDevice(ushort fileHandle, ushort readLength, uint targetAddress) {
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
            byte[] data = _memory.GetSlice((int)bufferAddress, writeLength).ToArray();
            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                string valueAsString = _dosStringDecoder.ConvertDosChars(data);
                _loggerService.Verbose("Writing to file or device content: {Name} {Bytes} {CodePage850String}",
                    file.Name, data.ToArray(), valueAsString);
            }

            file.Write(data);
        } catch (IOException e) {
            throw new UnrecoverableException("IOException while writing file", e);
        }

        return DosFileOperationResult.Value16(writeLength);
    }

    private bool TryUpdateDosTransferAreaWithFileMatch(DosDiskTransferArea dta,
    string fileSpec, string matchingFileName, string hostFolder, out DosFileOperationResult status) {
        try {
            UpdateDosTransferAreaWithFileMatch(dta, fileSpec, matchingFileName, hostFolder);
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

    private int CountHandles(DosFile openFileToCount) => OpenFiles.Count(openFile => openFile == openFileToCount);

    private DosFileOperationResult FileAccessDeniedError(string? filename) {
        return FileOperationErrorWithLog($"File {filename} already in use!", DosErrorCode.AccessDenied);
    }

    private DosFileOperationResult FileNotFoundError(string? fileName) {
        return FileOperationErrorWithLog($"File {fileName} not found!", DosErrorCode.FileNotFound);
    }

    private DosFileOperationResult PathNotFoundError(string? path) {
        return FileOperationErrorWithLog($"File {path} not found!", DosErrorCode.PathNotFound);
    }

    private DosFileOperationResult RemoveCurrentDirError(string? path) {
        return FileOperationErrorWithLog($"Attempted to remove current dir {path}",
            DosErrorCode.AttemptedToRemoveCurrentDirectory);
    }

    private DosFileOperationResult FileOperationErrorWithLog(string message, DosErrorCode errorCode) {
        if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
            _loggerService.Warning("{FileNotFoundErrorWithLog}: {Message}", nameof(FileOperationErrorWithLog), message);
        }

        return DosFileOperationResult.Error(errorCode);
    }

    private DosFileOperationResult FileNotOpenedError(int fileHandle) {
        if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
            _loggerService.Warning("File not opened: {FileHandle}", fileHandle);
        }
        return DosFileOperationResult.Error(DosErrorCode.InvalidHandle);
    }

    private int? FindNextFreeFileIndex() {
        for (int i = 0; i < OpenFiles.Length; i++) {
            if (OpenFiles[i] == null) {
                return i;
            }
        }

        return null;
    }

    private uint GetDiskTransferAreaPhysicalAddress() => MemoryUtils.ToPhysicalAddress(
        _diskTransferAreaAddressSegment, _diskTransferAreaAddressOffset);

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
        return DosFileOperationResult.Error(DosErrorCode.TooManyOpenFiles);
    }

    /// <summary>
    /// Resolves a DOS path to a host file system path.
    /// </summary>
    /// <param name="dosPath">The DOS path to resolve.</param>
    /// <returns>The resolved host path, or null if the path cannot be resolved.</returns>
    public string? GetHostPath(string dosPath) => _dosPathResolver.GetFullHostPathFromDosOrDefault(dosPath);

    internal string? TryGetFullHostPathFromDos(string dosPath) => GetHostPath(dosPath);

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

    private ushort ComputeDefaultDeviceInformation(DosFile dosFile) {
        byte driveIndex = dosFile.Drive == 0xff ? _dosDriveManager.CurrentDriveIndex : dosFile.Drive;
        bool isRemovable = driveIndex <= 1;
        bool isRemote = false;
        if (_dosDriveManager.ElementAtOrDefault(driveIndex).Value is { } drive) {
            isRemovable = drive.IsRemovable;
            isRemote = drive.IsRemote;
        }

        ushort info = (ushort)(driveIndex & 0x3F);
        if (!isRemovable) {
            info |= 0x0800;
        }

        if (isRemote) {
            info |= 0x1000;
            info |= 0x8000;
        }

        return info;
    }

    private DosFileOperationResult OpenFileInternal(string dosFileName, string? hostFileName, FileAccessMode openMode, bool noInherit = false) {
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
            // Extract just the access mode bits (0-2) for file open operations
            FileAccessMode baseAccessMode = (FileAccessMode)((byte)openMode & 0b111);
            switch (baseAccessMode) {
                case FileAccessMode.ReadOnly: {
                        if (File.Exists(hostFileName)) {
                            randomAccessFile = File.Open(hostFileName,
                                FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        } else {
                            return FileNotFoundError(dosFileName);
                        }

                        break;
                    }
                case FileAccessMode.WriteOnly:
                    randomAccessFile = File.Open(hostFileName, FileMode.OpenOrCreate,
                        FileAccess.Write, FileShare.ReadWrite);
                    break;
                case FileAccessMode.ReadWrite: {
                        if (File.Exists(hostFileName)) {
                            randomAccessFile = File.Open(hostFileName, FileMode.Open,
                                FileAccess.ReadWrite, FileShare.ReadWrite);
                        } else {
                            return FileNotFoundError(dosFileName);
                        }
                        break;
                    }
            }

            if (randomAccessFile != null) {
                byte driveIndex = _dosDriveManager.CurrentDriveIndex;
                DosFile dosFile = new(dosFileName, dosIndex, randomAccessFile) {
                    Drive = driveIndex,
                    Flags = noInherit ? (byte)FileAccessMode.Private : (byte)0
                };
                dosFile.DeviceInformation = ComputeDefaultDeviceInformation(dosFile);
                SetOpenFile(dosIndex, dosFile);
            }
        } catch (FileNotFoundException) {
            return FileNotFoundError(dosFileName);
        } catch (IOException) {
            return FileAccessDeniedError(dosFileName);
        }

        return DosFileOperationResult.Value16(dosIndex);
    }

    private void SetOpenFile(ushort fileHandle, VirtualFileBase? openFile) => OpenFiles[fileHandle] = openFile;

    private void UpdateDosTransferAreaWithFileMatch(DosDiskTransferArea dta, string fileSpec,
        string matchingFileSystemEntry, string searchFolder) {
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("Found matching file {MatchingFileSystemEntry}", matchingFileSystemEntry);
        }

        FileSystemInfo entryInfo = Directory.Exists(matchingFileSystemEntry) ?
            new DirectoryInfo(matchingFileSystemEntry) : new FileInfo(matchingFileSystemEntry);
        DateTime creationZonedDateTime = entryInfo.CreationTimeUtc;
        DateTime creationLocalDate = creationZonedDateTime.ToLocalTime();
        DateTime creationLocalTime = creationZonedDateTime.ToLocalTime();
        DosFileAttributes dosAttributes = (DosFileAttributes)entryInfo.Attributes;
        dta.FileAttributes = (byte)dosAttributes;
        dta.FileDate = ToDosDate(creationLocalDate);
        dta.FileTime = ToDosTime(creationLocalTime);
        if (entryInfo is FileInfo fileInfo) {
            dta.FileSize = (uint)fileInfo.Length;
        } else {
            // The FAT node entry size for a directory
            dta.FileSize = 4096;
        }
        dta.FileName = DosPathResolver.GetShortFileName(Path.GetFileName(matchingFileSystemEntry), searchFolder);
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
        IoctlFunction function = (IoctlFunction)state.AL;
        string operationName = $"IOCTL function {function} (0x{state.AL:X2})";

        if (function is IoctlFunction.GetDeviceInformation or IoctlFunction.SetDeviceInformation or
            IoctlFunction.ReadFromControlChannel or IoctlFunction.WriteToControlChannel or
            IoctlFunction.GetInputStatus or IoctlFunction.GetOutputStatus or
            IoctlFunction.IsHandleRemote or IoctlFunction.GenericIoctlForCharacterDevices or
            IoctlFunction.QueryGenericIoctlCapabilityForHandle) {
            handle = (byte)state.BX;
            if (handle >= OpenFiles.Length || OpenFiles[handle] == null) {
                if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                    _loggerService.Warning("IOCTL: Invalid handle {Handle} for {Operation}", handle, operationName);
                }
                return DosFileOperationResult.Error(DosErrorCode.InvalidHandle);
            }
        } else if ((byte)function <= (byte)IoctlFunction.QueryGenericIoctlCapabilityForBlockDevice) {
            if (function != IoctlFunction.SetSharingRetryCount) {
                drive = (byte)(state.BX == 0 ? _dosDriveManager.CurrentDriveIndex : state.BX - 1);
                if (drive >= 2 && (drive >= _dosDriveManager.NumberOfPotentiallyValidDriveLetters ||
                    _dosDriveManager.Count < (drive + 1))) {
                    if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                        _loggerService.Warning("IOCTL: Invalid drive {Drive} for {Operation}",
                            drive, operationName);
                    }
                    return DosFileOperationResult.Error(DosErrorCode.InvalidDrive);
                }
            }
        } else {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("IOCTL: Function number 0x{Function:X2} is invalid or unsupported", state.AL);
            }
            return DosFileOperationResult.Error(DosErrorCode.FunctionNumberInvalid);
        }

        switch (function) {
            case IoctlFunction.GetDeviceInformation:
                VirtualFileBase? fileOrDevice = OpenFiles[handle];
                if (fileOrDevice is IVirtualDevice virtualDevice) {
                    state.DX = (ushort)(virtualDevice.Information & ~ExtDeviceBit);
                } else if (fileOrDevice is DosFile dosFile) {
                    if (dosFile.Drive == 0xff) {
                        _loggerService.Warning("IOCTL: No drive set for file handle {FileHandle}, defaulting to C:",
                            handle);
                        dosFile.Drive = 0x2;
                    }

                    if (dosFile.DeviceInformation == 0) {
                        dosFile.DeviceInformation = ComputeDefaultDeviceInformation(dosFile);
                    }

                    state.DX = dosFile.DeviceInformation;
                    return DosFileOperationResult.Value16(dosFile.DeviceInformation);
                }
                return DosFileOperationResult.Value16(state.DX);

            case IoctlFunction.SetDeviceInformation:
                if (state.DH != 0) {
                    if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                        _loggerService.Warning("IOCTL: Invalid data for Set Device Information - DH={DH:X2}", state.DH);
                    }
                    return DosFileOperationResult.Error(DosErrorCode.DataInvalid);
                } else {
                    if (OpenFiles[handle] is IVirtualDevice device && (device.Information & 0x8000) > 0) {
                        state.AL = device.GetStatus(true);
                    } else {
                        if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                            _loggerService.Warning("IOCTL: Device for handle {Handle} doesn't support Set Device Information", handle);
                        }
                        return DosFileOperationResult.Error(DosErrorCode.FunctionNumberInvalid);
                    }
                }
                return DosFileOperationResult.NoValue();

            case IoctlFunction.ReadFromControlChannel:
                if (OpenFiles[handle] is IVirtualDevice readDevice &&
                    (readDevice.Information & 0xc000) > 0) {
                    if (readDevice is PrinterDevice printer && !printer.CanRead) {
                        if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                            _loggerService.Warning("IOCTL: Printer device {Device} doesn't support reading", printer.Name);
                        }
                        return DosFileOperationResult.Error(DosErrorCode.AccessDenied);
                    }
                    uint buffer = MemoryUtils.ToPhysicalAddress(state.DS, state.DX);
                    if (readDevice.TryReadFromControlChannel(buffer, state.CX, out ushort? returnCode)) {
                        state.AX = returnCode.Value;
                        return DosFileOperationResult.NoValue();
                    }
                }
                if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                    _loggerService.Warning("IOCTL: Device for handle {Handle} doesn't support reading from control channel", handle);
                }
                return DosFileOperationResult.Error(DosErrorCode.FunctionNumberInvalid);

            case IoctlFunction.WriteToControlChannel:
                if (OpenFiles[handle] is IVirtualDevice writtenDevice &&
                    (writtenDevice.Information & 0xc000) > 0) {
                    /* is character device with IOCTL support */
                    if (writtenDevice is PrinterDevice printer && !printer.CanWrite) {
                        if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                            _loggerService.Warning("IOCTL: Printer device {Device} doesn't support writing", printer.Name);
                        }
                        return DosFileOperationResult.Error(DosErrorCode.AccessDenied);
                    }
                    uint buffer = MemoryUtils.ToPhysicalAddress(state.DS, state.DX);
                    if (writtenDevice.TryWriteToControlChannel(buffer, state.CX, out ushort? returnCode)) {
                        state.AX = returnCode.Value;
                        return DosFileOperationResult.NoValue();
                    }
                }
                if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                    _loggerService.Warning("IOCTL: Device for handle {Handle} doesn't support writing to control channel", handle);
                }
                return DosFileOperationResult.Error(DosErrorCode.FunctionNumberInvalid);

            case IoctlFunction.GetInputStatus:
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
                    file.Seek(0, SeekOrigin.End);
                    long endLocation = file.Position;
                    if (oldLocation < endLocation) { //Still data available
                        state.AL = 0xff;
                    } else {
                        state.AL = 0x0; //EOF or beyond
                    }
                    file.Seek(oldLocation, SeekOrigin.Begin); //restore filelocation
                    if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                        _loggerService.Verbose("IOCTL: Get input status on regular file with handle {Handle}, available={Available}",
                            handle, state.AL != 0);
                    }
                }
                return DosFileOperationResult.NoValue();

            case IoctlFunction.GetOutputStatus:
                if (OpenFiles[handle] is IVirtualDevice outputDevice &&
                    (outputDevice.Information & ExtDeviceBit) > 0) {
                    state.AL = outputDevice.GetStatus(false);
                }
                if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                    _loggerService.Verbose("IOCTL: Get output status for handle {Handle}, reporting ready (0xFF)", handle);
                }
                state.AL = 0xFF;
                return DosFileOperationResult.NoValue();

            case IoctlFunction.IsBlockDeviceRemovable:
                //* cdrom drives and drive A and B are removable */
                if (drive < 2) {
                    state.AX = 0;
                } else if (!_dosDriveManager.ElementAtOrDefault(drive).Value.IsRemovable) {
                    state.AX = 1;
                } else {
                    if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                        _loggerService.Warning("IOCTL: Unable to determine if drive {Drive} is removable", drive);
                    }
                    return DosFileOperationResult.Error(DosErrorCode.FunctionNumberInvalid);
                }
                return DosFileOperationResult.NoValue();

            case IoctlFunction.IsBlockDeviceRemote:
                if ((drive >= 2) && _dosDriveManager.ElementAt(drive).Value.IsRemote) {
                    state.DX = 0x1000;  // device is remote
                                        // undocumented bits always clear
                } else {
                    state.DX = 0x0802;  // Open/Close supported; 32bit access supported (any use? fixes Fable installer)
                                        // undocumented bits from device attribute word
                                        // TODO Set bit 9 on drives that don't support direct I/O
                }
                return DosFileOperationResult.NoValue();

            case IoctlFunction.SetSharingRetryCount:
                if (state.DX == 0) {
                    if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                        _loggerService.Warning("IOCTL: Invalid retry count 0 for Set sharing retry count");
                    }
                    return DosFileOperationResult.Error(DosErrorCode.FunctionNumberInvalid);
                }
                return DosFileOperationResult.NoValue();

            case IoctlFunction.GenericIoctlForBlockDevices:
                if (drive < 2 && _dosDriveManager.ElementAtOrDefault(drive).Value is null) {
                    if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                        _loggerService.Warning("IOCTL: Access denied for drive {Drive} - drive not available", drive);
                    }
                    return DosFileOperationResult.Error(DosErrorCode.AccessDenied);
                }
                if (state.CH != 0x08 || _dosDriveManager.ElementAtOrDefault(drive).Value.IsRemovable) {
                    if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                        _loggerService.Warning("IOCTL: Invalid or unsupported command 0x{Command:X2} for drive {Drive}",
                            state.CH, drive);
                    }
                    return DosFileOperationResult.Error(DosErrorCode.FunctionNumberInvalid);
                }

                SegmentedAddress parameterBlock = new(state.DS, state.DX);

                GenericBlockDeviceCommand blockCommand = (GenericBlockDeviceCommand)state.CL;
                switch (blockCommand) {
                    case GenericBlockDeviceCommand.GetDeviceParameters:
                        DosDeviceParameterBlock dosDeviceParameterBlock = new(_memory, parameterBlock.Linear);
                        dosDeviceParameterBlock.DeviceType = (byte)(drive >= 2 ? 0x05 : 0x07);
                        dosDeviceParameterBlock.DeviceAttributes = (ushort)(drive >= 2 ? 0x01 : 0x00);
                        dosDeviceParameterBlock.Cylinders = 0;
                        dosDeviceParameterBlock.MediaType = 0;
                        dosDeviceParameterBlock.BiosParameterBlock.BytesPerSector = 0x0200; // (Win3 File Mgr. uses it)
                        break;

                    case GenericBlockDeviceCommand.SetVolumeSerialNumber:
                        // TODO: pull new serial from DS:DX buffer and store it somewhere
                        if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                            _loggerService.Warning("IOCTL: Set Volume Serial Number called but not yet implemented for drive {Drive}", drive);
                        }
                        break;

                    case GenericBlockDeviceCommand.GetVolumeSerialNumber: {
                            VirtualDrive vDrive = _dosDriveManager.ElementAtOrDefault(drive).Value;
                            DosVolumeInfo dosVolumeInfo = new(_memory, parameterBlock.Linear);
                            dosVolumeInfo.SerialNumber = 0x1234;
                            dosVolumeInfo.VolumeLabel = vDrive.Label.ToUpperInvariant();
                            dosVolumeInfo.FileSystemType = drive < 2 ? "FAT12" : "FAT16";
                            break;
                        }

                    default:
                        if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                            _loggerService.Error(
                              "IOCTL: Unhandled Generic Block Device request CL=0x{Command:X2} for drive {Drive}",
                              state.CL, drive);
                        }
                        return DosFileOperationResult.Error(DosErrorCode.FunctionNumberInvalid);
                }
                state.AX = 0;
                return DosFileOperationResult.NoValue();

            case IoctlFunction.GetLogicalDriveMap:
                if (drive < 2) {
                    if (_dosDriveManager.HasDriveAtIndex(drive)) {
                        state.AL = (byte)(drive + 1);
                    } else {
                        state.AL = 1;
                    }
                } else if (_dosDriveManager.ElementAtOrDefault(drive).Value.IsRemovable) {
                    if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                        _loggerService.Warning("IOCTL: Get Logical Drive Map not supported for removable drive {Drive}", drive);
                    }
                    return DosFileOperationResult.Error(DosErrorCode.FunctionNumberInvalid);
                } else { /* Only 1 logical drive assigned */
                    state.AL = 0;
                    state.AH = 0x07;
                }
                return DosFileOperationResult.NoValue();

            case IoctlFunction.IsHandleRemote:
                // Check if handle refers to a remote file/device
                // DX bit 15 is set if handle is remote
                VirtualFileBase? remoteCheckFile = OpenFiles[handle];
                if (remoteCheckFile is IVirtualDevice) {
                    // Character devices are local
                    state.DX = 0;
                    state.AX = 0;
                } else if (remoteCheckFile is DosFile remoteFile) {
                    // Check if file is on a remote drive
                    byte fileDrive = remoteFile.Drive == 0xff ? _dosDriveManager.CurrentDriveIndex : remoteFile.Drive;
                    state.DX = _dosDriveManager.ElementAtOrDefault(fileDrive).Value?.IsRemote == true ? (ushort)0x8000 : (ushort)0;
                    state.AX = 0;
                } else {
                    // Unexpected file type or null; set default values and log warning
                    state.DX = 0;
                    state.AX = 0;
                    if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                        _loggerService.Warning("IOCTL: IsHandleRemote called for unexpected file type {Type} at handle {Handle}", remoteCheckFile?.GetType().FullName ?? "null", handle);
                    }
                }
                return DosFileOperationResult.NoValue();

            default:
                if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                    _loggerService.Error("IOCTL: Invalid function number 0x{FunctionCode:X2}", state.AL);
                }
                return DosFileOperationResult.Error(DosErrorCode.FunctionNumberInvalid);
        }
    }

    /// <summary>
    /// Converts a host file path to a proper DOS path for the emulated program.
    /// </summary>
    /// <param name="hostPath">The absolute host path to the executable file.</param>
    /// <returns>A properly formatted DOS absolute path for the PSP env block.</returns>
    /// <remarks>
    /// This method implements the DOS TRUENAME functionality to convert a host path
    /// to a canonical DOS path. It finds the mounted drive that contains the host path
    /// and constructs the full DOS path including the directory structure.
    /// 
    /// For example, if the C: drive is mounted at "/home/user/games" and the host path
    /// is "/home/user/games/MYFOLDER/GAME.EXE", the result will be "C:\MYFOLDER\GAME.EXE".
    /// 
    /// This is critical for programs that need to find resources relative to their
    /// own executable location (like VB3 runtimes embedded in the EXE).
    /// </remarks>
    public string GetDosProgramPath(string hostPath) {
        // Normalize the host path once before iterating through drives
        string normalizedHostPath = ConvertUtils.ToSlashPath(hostPath);
        
        // Try to find a mounted drive that contains this host path
        foreach (VirtualDrive drive in _dosDriveManager.GetDrives()) {
            string mountedDir = ConvertUtils.ToSlashPath(drive.MountedHostDirectory).TrimEnd('/');
            
            // Check if the host path starts with the mounted directory
            // Ensure we match exact directory boundaries to avoid false positives
            // (e.g., "/home/user/games" should not match "/home/user/gamesdir/file.exe")
            if (normalizedHostPath.StartsWith(mountedDir, StringComparison.OrdinalIgnoreCase) &&
                (normalizedHostPath.Length == mountedDir.Length || normalizedHostPath[mountedDir.Length] == '/')) {
                // Get the relative path from the mount point
                string relativePath = normalizedHostPath.Length > mountedDir.Length 
                    ? normalizedHostPath[(mountedDir.Length + 1)..] // Skip the separator
                    : "";
                
                // Convert to DOS path format using existing utilities
                string dosRelativePath = ConvertUtils.ToBackSlashPath(relativePath);
                string dosPath = string.IsNullOrEmpty(dosRelativePath)
                    ? $"{drive.DosVolume}\\"
                    : $"{drive.DosVolume}\\{dosRelativePath}";
                
                // Normalize to uppercase for DOS compatibility
                dosPath = dosPath.ToUpperInvariant();
                
                if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                    _loggerService.Debug("GetDosProgramPath: Converted host path '{HostPath}' to DOS path '{DosPath}'",
                        hostPath, dosPath);
                }
                
                return dosPath;
            }
        }
        
        // No matching drive found - this is an error condition
        throw new InvalidOperationException($"No mounted drive contains the host path '{hostPath}'");
    }
}