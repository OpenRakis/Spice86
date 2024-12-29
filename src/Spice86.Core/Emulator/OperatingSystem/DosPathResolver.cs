namespace Spice86.Core.Emulator.OperatingSystem;

using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Utils;

using System.IO;
using System.Linq;
using System.Text;

/// <summary>
/// Translates DOS filepaths to host file paths, and vice-versa.
/// </summary>
internal class DosPathResolver {
    internal const char VolumeSeparatorChar = ':';
    internal const char DirectorySeparatorChar = '\\';
    internal const char AltDirectorySeparatorChar = '/';
    private const int MaxPathLength = 255;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="cDriveFolderPath">The host path to be mounted as C:.</param>
    /// <param name="executablePath">The host path to the DOS executable to be launched.</param>
    /// <param name="currentDrive">The drive in use on emulator startup. Defaults to C.</param>
    public DosPathResolver(string? cDriveFolderPath, string? executablePath, char currentDrive = 'C') {
        _driveMap = InitializeDriveMap(cDriveFolderPath, executablePath);
        _currentDrive = currentDrive;
        SetCurrentDirValue(_currentDrive, _driveMap[_currentDrive].MountedHostDirectory, $@"{currentDrive}:\");
    }

    private readonly Dictionary<char, MountedFolder> _driveMap;

    /// <summary>
    /// The current DOS drive in use.
    /// </summary>
    private char _currentDrive;

    /// <summary>
    /// Gets the current DOS directory.
    /// </summary>
    public DosFileOperationResult GetCurrentDosDirectory(byte driveNumber, out string currentDir) {
        //0 = default drive
        if (driveNumber == 0 && _driveMap.Count > 0) {
            MountedFolder mountedFolder = _driveMap[_currentDrive];
            currentDir = mountedFolder.CurrentDosDirectory;
            return DosFileOperationResult.NoValue();
        } else if (_driveMap.TryGetValue(DriveLetters[driveNumber - 1], out MountedFolder? mountedFolder)) {
            currentDir = mountedFolder.CurrentDosDirectory;
            return DosFileOperationResult.NoValue();
        }
        currentDir = "";
        return DosFileOperationResult.Error(ErrorCode.InvalidDrive);
    }

    private static string GetFullCurrentDosPathOnDrive(MountedFolder mountedFolder) => Path.Combine($"{mountedFolder.DosDriveRootPath}{DosPathResolver.DirectorySeparatorChar}", mountedFolder.CurrentDosDirectory);

    private static string GetExeParentFolder(string? exe) {
        string fallbackValue = ConvertUtils.ToSlashFolderPath(Environment.CurrentDirectory);
        if (string.IsNullOrWhiteSpace(exe)) {
            return fallbackValue;
        }
        string? parent = Path.GetDirectoryName(exe);
        return string.IsNullOrWhiteSpace(parent) ? fallbackValue : ConvertUtils.ToSlashFolderPath(parent);
    }

    private static Dictionary<char, MountedFolder> InitializeDriveMap(string? cDriveFolderPath, string? executablePath) {
        Dictionary<char, MountedFolder> driveMap = new();
        if (string.IsNullOrWhiteSpace(cDriveFolderPath)) {
            cDriveFolderPath = GetExeParentFolder(executablePath);
        }
        cDriveFolderPath = ConvertUtils.ToSlashFolderPath(cDriveFolderPath);
        driveMap.Add('C', new MountedFolder('C', cDriveFolderPath));
        return driveMap;
    }

    private static bool IsWithinMountPoint(string hostFullPath, MountedFolder mountedFolder) => hostFullPath.StartsWith(mountedFolder.MountedHostDirectory);

    /// <summary>
    /// Sets the current DOS folder.
    /// </summary>
    /// <param name="dosPath">The new DOS path to use as the current DOS folder.</param>
    /// <returns>A <see cref="DosFileOperationResult"/> that details the result of the operation.</returns>
    public DosFileOperationResult SetCurrentDir(string dosPath) {
        string fullDosPath = GetFullDosPathIncludingRoot(dosPath);

        if (!StartsWithDosDriveAndVolumeSeparator(fullDosPath)) {
            return DosFileOperationResult.Error(ErrorCode.PathNotFound);
        }

        string? hostPath = GetFullHostPathFromDosOrDefault(fullDosPath);
        if (!string.IsNullOrWhiteSpace(hostPath)) {
            return SetCurrentDirValue(fullDosPath[0], hostPath, fullDosPath);
        } else {
            return DosFileOperationResult.Error(ErrorCode.PathNotFound);
        }
    }

    private string GetDosDrivePathFromDosPath(string absoluteOrRelativeDosPath) {
        if (IsPathRooted(absoluteOrRelativeDosPath)) {
            if (StartsWithDosDriveAndVolumeSeparator(absoluteOrRelativeDosPath)) {
                return $"{absoluteOrRelativeDosPath[0]}{VolumeSeparatorChar}";
            }
        }
        return _driveMap[_currentDrive].DosDriveRootPath;
    }

    private DosFileOperationResult SetCurrentDirValue(char driveLetter, string? hostFullPath, string fullDosPath) {
        if (string.IsNullOrWhiteSpace(hostFullPath) ||
            !IsWithinMountPoint(hostFullPath, _driveMap[driveLetter]) ||
            Encoding.ASCII.GetByteCount(fullDosPath) > MaxPathLength) {
            return DosFileOperationResult.Error(ErrorCode.PathNotFound);
        }

        _driveMap[driveLetter].CurrentDosDirectory = fullDosPath[3..];
        return DosFileOperationResult.NoValue();
    }

    private string GetFullDosPathIncludingRoot(string absoluteOrRelativeDosPath) {
        if(string.IsNullOrWhiteSpace(absoluteOrRelativeDosPath)) {
            return absoluteOrRelativeDosPath;
        }
        StringBuilder normalizedDosPath = new();

        string backslashedDosPath = ConvertUtils.ToBackSlashPath(absoluteOrRelativeDosPath);

        string driveRoot = $"{GetDosDrivePathFromDosPath(backslashedDosPath)}{DirectorySeparatorChar}";
        normalizedDosPath.Append(driveRoot);

        if(backslashedDosPath.StartsWith(driveRoot)) {
            backslashedDosPath = backslashedDosPath[3..];
        }
        else if (backslashedDosPath.StartsWith(driveRoot[..2])) {
            backslashedDosPath = backslashedDosPath[2..];
        }

        IEnumerable<string> pathElements = backslashedDosPath.Split(DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        bool moveNext = false;
        bool appendedFolder = false;
        bool mustPrependDirectorySeparator = false;
        foreach (string pathElement in pathElements) {
            if(pathElement == ".." && appendedFolder) {
                moveNext = true;
            }
            else {
                if(moveNext) {
                    moveNext = false;
                    continue;
                }
                if(pathElement != "." && pathElement != ".." && !pathElement.Contains(VolumeSeparatorChar)) {
                    appendedFolder = true;
                    if(mustPrependDirectorySeparator) {
                        normalizedDosPath.Append(DirectorySeparatorChar);
                    }
                    normalizedDosPath.Append(pathElement.ToUpperInvariant());
                    mustPrependDirectorySeparator = true;
                }
            }
        }

        return ConvertUtils.ToBackSlashPath(normalizedDosPath.ToString());
    }

    /// <summary>
    /// Converts the DOS path to a full host path of the parent directory.<br/>
    /// </summary>
    /// <param name="dosPath">The DOS path to convert.</param>
    /// <returns>A string containing the full path to the parent directory in the host file system, or <c>null</c> if nothing was found.</returns>
    public string? GetFullHostParentPathFromDosOrDefault(string dosPath) {
        string? parentPath = Path.GetDirectoryName(dosPath);
        if(string.IsNullOrWhiteSpace(parentPath)) {
            parentPath = GetFullCurrentDosPathOnDrive(_driveMap[_currentDrive]);
        }
        string? fullHostPath = GetFullHostPathFromDosOrDefault(parentPath);
        if (string.IsNullOrWhiteSpace(fullHostPath)) {
            return null;
        }
        return ConvertUtils.ToSlashFolderPath(fullHostPath);
    }

    private (string HostPrefixPath, string DosRelativePath) DeconstructDosPath(string dosPath) {
        if (IsPathRooted(dosPath)) {
            int length = 1;
            if (StartsWithDosDriveAndVolumeSeparator(dosPath)) {
                length = 3;
            }
            return (_driveMap[_currentDrive].MountedHostDirectory, dosPath[length..]);
        } else if (StartsWithDosDriveAndVolumeSeparator(dosPath)) {
            return (_driveMap[dosPath[0]].MountedHostDirectory, dosPath[2..]);
        } else {
            return (_driveMap[_currentDrive].MountedHostDirectory, dosPath);
        }
    }

    /// <summary>
    /// Converts the DOS path to a full host path.<br/>
    /// </summary>
    /// <param name="dosPath">The DOS path to convert.</param>
    /// <returns>A string containing the full file path in the host file system, or <c>null</c> if nothing was found.</returns>
    public string? GetFullHostPathFromDosOrDefault(string dosPath) {
        if (string.IsNullOrWhiteSpace(dosPath)) {
            return null;
        }
        dosPath = GetFullDosPathIncludingRoot(dosPath);

        (string HostPrefix, string DosRelativePath) = DeconstructDosPath(dosPath);

        if (string.IsNullOrWhiteSpace(DosRelativePath)) {
            return ConvertUtils.ToSlashPath(HostPrefix);
        }

        DirectoryInfo hostDirInfo = new DirectoryInfo(HostPrefix);

        string? relativeHostPath = hostDirInfo
            .EnumerateDirectories("*", new EnumerationOptions() {
                RecurseSubdirectories = true,
            })
            .Cast<FileSystemInfo>()
            .Concat(
            hostDirInfo.EnumerateFiles("*", new EnumerationOptions() {
                RecurseSubdirectories = true,
            }))
            .FirstOrDefault(x => IsRelativeHostFileOrFolderPathEqualIgnoreCase(x, HostPrefix, DosRelativePath))?.FullName;

        if (string.IsNullOrWhiteSpace(relativeHostPath)) {
            return null;
        }

        return ConvertUtils.ToSlashPath(Path.Combine(HostPrefix, relativeHostPath));
    }

    private static bool IsRelativeHostFileOrFolderPathEqualIgnoreCase(FileSystemInfo fileOrDirInfo, string hostPrefix, string dosRelativePath) {
        string relativePath = fileOrDirInfo.FullName[hostPrefix.Length..];
        if (fileOrDirInfo is FileInfo) {
            return string.Equals(ConvertUtils.ToSlashPath(relativePath),
                ConvertUtils.ToSlashPath(dosRelativePath),
                    StringComparison.OrdinalIgnoreCase);
        } else {
            return string.Equals(ConvertUtils.ToSlashFolderPath(relativePath),
                ConvertUtils.ToSlashFolderPath(dosRelativePath),
                    StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Prefixes the given DOS path by either the mapped drive folder or the current host folder depending on whether there is a root in the path.<br/>
    /// Does not convert to a case sensitive path. <br/>
    /// Does not search for the file or folder on disk.
    /// </summary>
    /// <param name="dosPath">The DOS path to convert.</param>
    /// <returns>A string containing the combination of the host path and the DOS path.</returns>
    public string PrefixWithHostDirectory(string dosPath) {
        if (string.IsNullOrWhiteSpace(dosPath)) {
            return dosPath;
        }
        dosPath = GetFullDosPathIncludingRoot(dosPath);
        (string HostPrefix, string DosRelativePath) = DeconstructDosPath(dosPath);
        return ConvertUtils.ToSlashPath(Path.Combine(HostPrefix, DosRelativePath));
    }

    /// <summary>
    /// All the possible DOS drive letters
    /// </summary>
    private static char[] DriveLetters => new[] { 'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J', 'K', 'L', 'M', 'N', 'O', 'P', 'Q', 'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z' };

    /// <summary>
    /// Gets or sets the <see cref="_currentDrive"/> with a byte value (0x0: A:, 0x1: B:, ...)
    /// </summary>
    public byte CurrentDriveIndex {
        get => (byte)Array.IndexOf(DriveLetters, _currentDrive);
        set {
            // Where in Space is Carmen Sandiego ? tries to set this to 119...
            if (value < (DriveLetters.Length - 1)) {
                _currentDrive = DriveLetters[value];
            }
        }
    }

    public byte NumberOfPotentiallyValidDriveLetters {
        get {
            // At least A: and B:
            byte driveLetters = 2;
            driveLetters += (byte)_driveMap.TakeWhile(x => x.Key != 'A' && x.Key != 'B').Count();
            return driveLetters;
        }
    }

    private bool StartsWithDosDriveAndVolumeSeparator(string dosPath) =>
        dosPath.Length >= 2 &&
        DriveLetters.Contains(char.ToUpperInvariant(dosPath[0])) &&
        dosPath[1] == VolumeSeparatorChar;

    private bool IsPathRooted(string path) =>
        path.StartsWith(DirectorySeparatorChar) ||
        path.StartsWith(AltDirectorySeparatorChar) ||
        (path.Length >= 3 &&
        StartsWithDosDriveAndVolumeSeparator(path) &&
        path[2] == DirectorySeparatorChar);

    /// <summary>
    /// Returns whether the folder or file name already exists, in DOS's case insensitive point of view.
    /// </summary>
    /// <param name="newFileOrDirectoryPath">The name of new file or folder we try to create.</param>
    /// <param name="hostFolder">The full path to the host folder to look into.</param>
    /// <returns>A boolean value indicating if there is any folder or file with the same name.</returns>
    public bool AnyDosDirectoryOrFileWithTheSameName(string newFileOrDirectoryPath, DirectoryInfo hostFolder) =>
        GetTopLevelDirsAndFiles(hostFolder.FullName).Any(x => string.Equals(x, newFileOrDirectoryPath, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<string> GetTopLevelDirsAndFiles(string hostPath, string searchPattern = "*") {
        return Directory
            .GetDirectories(hostPath, searchPattern)
            .Concat(Directory.GetFiles(hostPath, searchPattern));
    }
}