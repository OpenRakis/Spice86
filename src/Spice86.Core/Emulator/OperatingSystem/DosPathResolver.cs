namespace Spice86.Core.Emulator.OperatingSystem;

using System.IO;
using System.Linq;
using System.Text;

using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

/// <inheritdoc cref="IDosPathResolver" />
public class DosPathResolver : IDosPathResolver {
    private readonly ILoggerService _loggerService;

    /// <inheritdoc />
    public IDictionary<char, MountedFolder> DriveMap { get; private set; } = new Dictionary<char, MountedFolder>();

    /// <inheritdoc />
    public char CurrentDrive { get; private set; }

    /// <inheritdoc />
    public string CurrentHostDirectory => ConvertUtils.ToSlashPath(DriveMap[CurrentDrive].FullName);

    /// <inheritdoc />
    public DosFileOperationResult GetCurrentDosDirectory(byte driveNumber, out string currentDir) {
        //default drives
        if (driveNumber == 0 && DriveMap.Any()) {
            MountedFolder mountedFolder = DriveMap[CurrentDrive];
            currentDir = Path.GetRelativePath(mountedFolder.MountPoint, mountedFolder.CurrentDirectory).ToUpperInvariant();
            return DosFileOperationResult.NoValue();
        }
        else if (DriveMap.TryGetValue(DriveLetters[driveNumber - 1], out MountedFolder? mountedFolder)) {
            currentDir = Path.GetRelativePath(mountedFolder.MountPoint, mountedFolder.CurrentDirectory).ToUpperInvariant();
            return DosFileOperationResult.NoValue();
        }
        currentDir = "";
        return DosFileOperationResult.Error(ErrorCode.InvalidDrive);
    }

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="loggerService">The logger service implementation.</param>
    /// <param name="configuration">The emulator configuration.</param>
    public DosPathResolver(ILoggerService loggerService, Configuration configuration) {
        _loggerService = loggerService;
        DriveMap = InitializeDriveMap(configuration);
        CurrentDrive = 'C';
        SetCurrentDirValue(CurrentDrive, DriveMap[CurrentDrive].MountPoint);
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

    private IDictionary<char, MountedFolder> InitializeDriveMap(Configuration configuration) {
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

    /// <inheritdoc />
    public string GetRelativeHostPathToCurrentDirectory(string hostPath) => Path.GetRelativePath(CurrentHostDirectory, hostPath);

    private static bool IsWithinMountPoint(string hostFullPath, MountedFolder mountedFolder) =>
        hostFullPath.StartsWith(mountedFolder.MountPoint);

    /// <inheritdoc/>
    public DosFileOperationResult SetCurrentDir(string dosPath) {
        if (IsPathRooted(dosPath)) {
            string? hostPath = TryGetFullHostPathFromDos(dosPath);
            if (!string.IsNullOrWhiteSpace(hostPath)) {
                char driveLetter = StartsWithDosDrive(dosPath) ? dosPath[0] : CurrentDrive;
                return SetCurrentDirValue(driveLetter, hostPath);
            } else {
                return DosFileOperationResult.Error(ErrorCode.PathNotFound);
            }
        }

        if (dosPath == "." || dosPath == @".\") {
            return DosFileOperationResult.NoValue();
        }

        if (dosPath == ".." || dosPath == @"..\") {
            string? newCurrentDir = Directory.GetParent(CurrentHostDirectory)?.FullName;
            return SetCurrentDirValue(CurrentDrive, newCurrentDir);
        }

        while (dosPath.StartsWith("..\\")) {
            dosPath = dosPath[3..];
            string? newCurrentDir = Directory.GetParent(CurrentHostDirectory)?.FullName;
            SetCurrentDirValue(CurrentDrive, newCurrentDir);
        }

        string? hostFullPath = TryGetFullHostPathFromDos(dosPath);
        return SetCurrentDirValue(CurrentDrive, hostFullPath);
    }

    private DosFileOperationResult SetCurrentDirValue(char driveLetter, string? hostFullPath) {
        if (string.IsNullOrWhiteSpace(hostFullPath) ||
            !IsWithinMountPoint(hostFullPath, DriveMap[driveLetter]) ||
            Encoding.ASCII.GetByteCount(hostFullPath) > 255) {
            return DosFileOperationResult.Error(ErrorCode.PathNotFound);
        }

        DriveMap[driveLetter].CurrentDirectory = hostFullPath;
        return DosFileOperationResult.NoValue();
    }

    /// <inheritdoc />
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
            int length = 1;
            if (StartsWithDosDrive(dosPath)) {
                length = 3;
            }
            return (DriveMap[dosPath[0]].MountPoint, dosPath[length..]);
        } else if (StartsWithDosDrive(dosPath)) {
            return (DriveMap[dosPath[0]].MountPoint, dosPath[2..]);
        } else {
            return (DriveMap[CurrentDrive].MountPoint, dosPath);
        }
    }

    /// <inheritdoc />
    public string? TryGetFullHostPathFromDos(string dosPath) {
        if(string.IsNullOrWhiteSpace(dosPath)) {
            return null;
        }
        (string HostPrefix, string DosRelativePath) = DeconstructDosPath(dosPath);

        if(string.IsNullOrWhiteSpace(DosRelativePath)) {
            return ConvertUtils.ToSlashPath(HostPrefix);
        }

        DirectoryInfo hostDirInfo = new DirectoryInfo(HostPrefix);

        string? relativeHostPath = hostDirInfo
            .EnumerateDirectories("*", new EnumerationOptions() {
                RecurseSubdirectories = true,
            })
            .Select(x => x.FullName)
            .Concat(
            hostDirInfo.EnumerateFiles("*",
            new EnumerationOptions() {
                RecurseSubdirectories = true,
            }).Select(x => x.FullName))
            .Select(x => x[HostPrefix.Length..])
            .OrderBy(x => x.Length)
            .FirstOrDefault(x => string.Equals(ConvertUtils.ToSlashPath(x), ConvertUtils.ToSlashPath(DosRelativePath), StringComparison.OrdinalIgnoreCase));

        if(string.IsNullOrWhiteSpace(relativeHostPath)) {
            return null;
        }

        return ConvertUtils.ToSlashPath(Path.Combine(HostPrefix, relativeHostPath));
    }

    /// <inheritdoc />
    public string PrefixWithHostDirectory(string dosPath) {
        if (string.IsNullOrWhiteSpace(dosPath)) {
            return dosPath;
        }
        (string HostPrefix, string DosRelativePath) = DeconstructDosPath(dosPath);
        return ConvertUtils.ToSlashPath(Path.Combine(HostPrefix, DosRelativePath));
    }

    /// <summary>
    /// All the possible DOS drive letters
    /// </summary>
    private static char[] DriveLetters => new char[] {'A','B','C','D','E','F','G','H','I','J','K','L','M','N','O','P','Q','R','S','T','U','V','W','X','Y','Z'};

    private bool StartsWithDosDrive(string path) =>
        path.Length >= 2 &&
        DriveLetters.Contains(char.ToUpperInvariant(path[0])) &&
        path[1] == ':';

    private bool IsPathRooted(string path) =>
        path.StartsWith(@"\") ||
        path.Length >= 3 &&
        StartsWithDosDrive(path) &&
        path[2] == '\\';

    /// <inheritdoc />
    public bool AnyDosDirectoryOrFileWithTheSameName(string newFileOrDirectoryPath, DirectoryInfo hostFolder) =>
        GetTopLevelDirsAndFiles(hostFolder.FullName).Any(x => string.Equals(x, newFileOrDirectoryPath, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<string> GetTopLevelDirsAndFiles(string hostPath, string searchPattern = "*") {
        return Directory
            .GetDirectories(hostPath, searchPattern)
            .Concat(Directory.GetFiles(hostPath, searchPattern));
    }
}