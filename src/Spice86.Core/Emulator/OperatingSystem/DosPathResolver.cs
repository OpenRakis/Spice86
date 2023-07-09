namespace Spice86.Core.Emulator.OperatingSystem;

using System.IO;
using System.Linq;
using System.Text;

using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Utils;

/// <summary>
/// Translates DOS filepaths to host file paths, and vice-versa.
/// </summary>
internal class DosPathResolver {
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="configuration">The emulator configuration.</param>
    public DosPathResolver(Configuration configuration) {
        _driveMap = InitializeDriveMap(configuration);
        _currentDrive = 'C';
        SetCurrentDirValue(_currentDrive, _driveMap[_currentDrive].MountedHostDirectory);
    }

    private readonly Dictionary<char, MountedFolder> _driveMap = new();

    /// <summary>
    /// The current DOS drive in use.
    /// </summary>
    private char _currentDrive;

    /// <summary>
    /// The full host path to the folder used by DOS as the current folder.
    /// </summary>
    private string CurrentHostDirectory => ConvertUtils.ToSlashPath(_driveMap[_currentDrive].FullHostCurrentDirectory);

    /// <summary>
    /// Gets the current DOS directory.
    /// </summary>
    public DosFileOperationResult GetCurrentDosDirectory(byte driveNumber, out string currentDir) {
        //0 = default drive
        if (driveNumber == 0 && _driveMap.Any()) {
            MountedFolder mountedFolder = _driveMap[_currentDrive];
            currentDir = mountedFolder.FullDosCurrentDirectory[mountedFolder.DosDriveRoot.Length..];
            return DosFileOperationResult.NoValue();
        }
        else if (_driveMap.TryGetValue(DriveLetters[driveNumber - 1], out MountedFolder? mountedFolder)) {
            currentDir = mountedFolder.FullDosCurrentDirectory[mountedFolder.DosDriveRoot.Length..];
            return DosFileOperationResult.NoValue();
        }
        currentDir = "";
        return DosFileOperationResult.Error(ErrorCode.InvalidDrive);
    }

    private static string GetExeParentFolder(Configuration configuration) {
        string? exe = configuration.Exe;
        string fallbackValue = ConvertUtils.ToSlashFolderPath(Environment.CurrentDirectory);
        if (string.IsNullOrWhiteSpace(exe)) {
            return fallbackValue;
        }
        string? parent = Path.GetDirectoryName(exe);
        return string.IsNullOrWhiteSpace(parent) ? fallbackValue : ConvertUtils.ToSlashFolderPath(parent);
    }

    private Dictionary<char, MountedFolder> InitializeDriveMap(Configuration configuration) {
        string parentFolder = GetExeParentFolder(configuration);
        Dictionary<char, MountedFolder> driveMap = new();
        string? cDrive = configuration.CDrive;
        if (string.IsNullOrWhiteSpace(cDrive)) {
            cDrive = parentFolder;
        }
        cDrive = ConvertUtils.ToSlashFolderPath(cDrive);
        driveMap.Add('C', new MountedFolder('C', cDrive));
        return driveMap;
    }

    /// <summary>
    /// Create a relative path from the current host directory to another. Paths will be resolved before calculating the difference.
    /// Default path comparison for the active platform will be used (OrdinalIgnoreCase for Windows or Mac, Ordinal for Unix).
    /// </summary>
    /// <param name="hostPath">The destination path.</param>
    /// <returns>A string containing the relative host path, or <paramref name="hostPath"/> if the paths don't share the same root.</returns>
    public string GetRelativeHostPathToCurrentDirectory(string hostPath) => Path.GetRelativePath(CurrentHostDirectory, hostPath);

    private static bool IsWithinMountPoint(string hostFullPath, MountedFolder mountedFolder) => hostFullPath.StartsWith(mountedFolder.MountedHostDirectory);

    /// <summary>
    /// Sets the current DOS folder.
    /// </summary>
    /// <param name="dosPath">The new DOS path to use as the current DOS folder.</param>
    /// <returns>A <see cref="DosFileOperationResult"/> that details the result of the operation.</returns>
    public DosFileOperationResult SetCurrentDir(string dosPath) {
        dosPath = GetFullDosPath(dosPath);

        if (!IsPathRooted(dosPath)) {
            return DosFileOperationResult.Error(ErrorCode.PathNotFound);
        }

        string? hostPath = TryGetFullHostPathFromDos(dosPath);
        if (!string.IsNullOrWhiteSpace(hostPath)) {
            return SetCurrentDirValue(dosPath[0], hostPath);
        } else {
            return DosFileOperationResult.Error(ErrorCode.PathNotFound);
        }
    }

    private string GetDosPathRoot(string dosPath) {
        if(IsPathRooted(dosPath)) {
            if(StartsWithDosDrive(dosPath)) {
                string? root = Path.GetPathRoot(dosPath);
                if(!string.IsNullOrWhiteSpace(root)) {
                    return root;
                }
            }
        }
        return _driveMap[_currentDrive].DosDriveRoot;
    }

    private string GetFullDosPath(string dosPath) => Path.GetFullPath(dosPath, GetDosPathRoot(dosPath));

    private DosFileOperationResult SetCurrentDirValue(char driveLetter, string? hostFullPath) {
        if (string.IsNullOrWhiteSpace(hostFullPath) ||
            !IsWithinMountPoint(hostFullPath, _driveMap[driveLetter]) ||
            Encoding.ASCII.GetByteCount(hostFullPath) > 255) {
            return DosFileOperationResult.Error(ErrorCode.PathNotFound);
        }

        _driveMap[driveLetter].FullHostCurrentDirectory = hostFullPath;
        return DosFileOperationResult.NoValue();
    }

    /// <summary>
    /// Converts the DOS path to a full host path of the parent directory.<br/>
    /// </summary>
    /// <param name="dosPath">The DOS path to convert.</param>
    /// <returns>A string containing the full path to the parent directory in the host file system, or <c>null</c> if nothing was found.</returns>
    public string? TryGetFullHostParentPathFromDos(string dosPath) {
        string? fullHostPath = TryGetFullHostPathFromDos(dosPath);
        if(string.IsNullOrWhiteSpace(fullHostPath)) {
            return null;
        }
        string? parent = Directory.GetParent(fullHostPath)?.FullName;
        if (string.IsNullOrWhiteSpace(parent)) {
            return null;
        }

        return ConvertUtils.ToSlashPath(parent);
    }

    private (string HostPrefixPath, string DosRelativePath) DeconstructDosPath(string dosPath) {
        if (IsPathRooted(dosPath)) {
            int length = dosPath.TakeWhile(x => x == '\\' || x == '/').Count();
            if (StartsWithDosDrive(dosPath)) {
                length = 3;
            }
            return (_driveMap[_currentDrive].MountedHostDirectory, dosPath[length..]);
        } else if (StartsWithDosDrive(dosPath)) {
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

    public string? TryGetFullHostPathFromDos(string dosPath) {
        if (string.IsNullOrWhiteSpace(dosPath)) {
            return null;
        }
        dosPath = GetFullDosPath(dosPath);

        (string HostPrefix, string DosRelativePath) = DeconstructDosPath(dosPath);

        if (string.IsNullOrWhiteSpace(DosRelativePath)) {
            return ConvertUtils.ToSlashPath(HostPrefix);
        }

        DirectoryInfo hostDirInfo = new DirectoryInfo(HostPrefix);

        string? relativeHostPath = hostDirInfo
            .EnumerateDirectories("*", new EnumerationOptions() {
                RecurseSubdirectories = true,
            })
            .Select(x => (FileSystemInfo)x)
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

    private static bool IsRelativeHostFileOrFolderPathEqualIgnoreCase(FileSystemInfo fileOrDirInfo, string HostPrefix, string DosRelativePath) {
        string relativePath = fileOrDirInfo.FullName[HostPrefix.Length..];
        if (fileOrDirInfo is FileInfo) {
            return string.Equals(ConvertUtils.ToSlashPath(relativePath),
                ConvertUtils.ToSlashPath(DosRelativePath),
                    StringComparison.OrdinalIgnoreCase);
        } else {
            return string.Equals(ConvertUtils.ToSlashFolderPath(relativePath),
                ConvertUtils.ToSlashFolderPath(DosRelativePath),
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
        dosPath = GetFullDosPath(dosPath);
        (string HostPrefix, string DosRelativePath) = DeconstructDosPath(dosPath);
        return ConvertUtils.ToSlashPath(Path.Combine(HostPrefix, DosRelativePath));
    }

    /// <summary>
    /// All the possible DOS drive letters
    /// </summary>
    private static char[] DriveLetters => new char[] {'A','B','C','D','E','F','G','H','I','J','K','L','M','N','O','P','Q','R','S','T','U','V','W','X','Y','Z'};

    /// <summary>
    /// Gets or sets the <see cref="_currentDrive"/> with a byte value (0x0: A:, 0x1: B:, ...)
    /// </summary>
    public byte CurrentDriveIndex {
        get  => (byte)Array.IndexOf(DriveLetters, _currentDrive);
        set => _currentDrive = DriveLetters[value];
    }

    public byte NumberOfPotentiallyValidDriveLetters => (byte)_driveMap.Count;

    private bool StartsWithDosDrive(string dosPath) =>
        dosPath.Length >= 2 &&
        DriveLetters.Contains(char.ToUpperInvariant(dosPath[0])) &&
        dosPath[1] == ':';

    private bool IsPathRooted(string path) =>
        path.StartsWith(@"\") ||
        path.StartsWith("/") ||
        path.Length >= 3 &&
        StartsWithDosDrive(path) &&
        path[2] == '\\';

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