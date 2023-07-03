namespace Spice86.Core.Emulator.OperatingSystem;

using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

/// <inheritdoc />
public class DosPathResolver : IDosPathResolver {

    /// <inheritdoc />
    public IDictionary<char, MountedFolder> DriveMap { get; private set; } = new Dictionary<char, MountedFolder>();

    /// <inheritdoc />
    public char CurrentDrive { get; private set; }

    /// <inheritdoc />
    public string CurrentHostDirectory => ConvertUtils.ToSlashPath(DriveMap[CurrentDrive].FullName);

    private readonly ILoggerService _loggerService;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="loggerService">The logger service implementation.</param>
    public DosPathResolver(ILoggerService loggerService) => _loggerService = loggerService;

    /// <inheritdoc />
    public string GetHostRelativePathToCurrentDirectory(string hostPath) => Path.GetRelativePath(CurrentHostDirectory, hostPath);

    private string GetSafeHostRelativePathToCurrentDirectory(string hostPath, string fallbackValue) {
        string newCurrentFolder = GetHostRelativePathToCurrentDirectory(hostPath);
        if(newCurrentFolder.StartsWith(@"..\") || newCurrentFolder.StartsWith("../")) {
            return fallbackValue;
        }
        return newCurrentFolder;
    }

    /// <inheritdoc/>
    public DosFileOperationResult SetCurrentDir(string dosPath) {
        if (IsPathRooted(dosPath)) {
            string? hostPath = TryGetFullHostPath(dosPath);
            if (!string.IsNullOrWhiteSpace(hostPath)) {
                char driveLetter = StartsWithDosDrive(dosPath) ? dosPath[0] : CurrentDrive;
                DriveMap[driveLetter].CurrentDirectory = GetSafeHostRelativePathToCurrentDirectory(hostPath, DriveMap[driveLetter].MountPoint);
                return DosFileOperationResult.NoValue();
            }
        }

        if (dosPath == "." || dosPath == @".\") {
            return DosFileOperationResult.NoValue();
        }

        if (dosPath == ".." || dosPath == @"..\") {
            DriveMap[CurrentDrive].CurrentDirectory = GetSafeHostRelativePathToCurrentDirectory(GetHostFullNameForParentDirectory(CurrentHostDirectory), DriveMap[CurrentDrive].MountPoint);
            return DosFileOperationResult.NoValue();
        }

        while (dosPath.StartsWith("..\\")) {
            dosPath = dosPath[3..];
            DriveMap[CurrentDrive].CurrentDirectory = GetSafeHostRelativePathToCurrentDirectory(GetHostFullNameForParentDirectory(CurrentHostDirectory), DriveMap[CurrentDrive].MountPoint);
        }

        while (dosPath.StartsWith(@"\")) {
            dosPath = dosPath[1..];
        }

        string? hostFullPath = TryGetFullHostPath(dosPath);

        if (string.IsNullOrWhiteSpace(hostFullPath)) {
            return DosFileOperationResult.Error(ErrorCode.PathNotFound);
        }
        DriveMap[CurrentDrive].CurrentDirectory = GetSafeHostRelativePathToCurrentDirectory(hostFullPath, DriveMap[CurrentDrive].MountPoint);
        return DosFileOperationResult.NoValue();
    }

    /// <inheritdoc/>
    public void SetDiskParameters(char currentDrive, string dosPath, IDictionary<char, MountedFolder> driveMap) {
        DriveMap = driveMap;
        CurrentDrive = currentDrive;
        SetCurrentDir(dosPath);
    }

    /// <inheritdoc />
    public string GetHostFullNameForParentDirectory(string hostPath) => Directory.GetParent(hostPath)?.FullName ?? hostPath;

    private string? RecursivelySearchForFullHostPath(string? dosPath, bool convertParentOnly) {
        if (string.IsNullOrWhiteSpace(dosPath)) {
            return null;
        }

        string hostPath = ConvertUtils.ToSlashPath(dosPath);
        string? parentDir = Path.GetDirectoryName(hostPath);
        if (File.Exists(hostPath) ||
            Directory.Exists(hostPath) ||
            convertParentOnly) {
            // file exists or root reached, no need to go further. Path found.
            return hostPath;
        }

        string? parent = RecursivelySearchForFullHostPath(parentDir, convertParentOnly);
        if (parent == null) {
            // End of recursion, root reached
            return null;
        }

        // Now that parent is for sure on the host file system, let's find the actual path
        try {
            string searchPattern = Path.GetFileName(hostPath);
            if (Directory.Exists(parent)) {
                return GetTopLevelDirsAndFiles(hostPath, searchPattern).FirstOrDefault();
            }
        } catch (IOException e) {
            e.Demystify();
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
                _loggerService.Warning(e, "Error while checking file {CaseInsensitivePath}: {Exception}", dosPath, e);
            }
        }

        return null;
    }

    /// <inheritdoc />
    public string? TryGetFullParentHostPath(string dosPath) {
        string fileName = PrefixWithHostDirectory(dosPath);

        string? parent = RecursivelySearchForFullHostPath(fileName, true);
        if (string.IsNullOrWhiteSpace(parent)) {
            return null;
        }

        return ConvertUtils.ToSlashPath(parent);
    }

    /// <inheritdoc />
    public string? TryGetFullHostPath(string dosPath) {
        string fileName = PrefixWithHostDirectory(dosPath);
        string? caseSensitivePath = RecursivelySearchForFullHostPath(fileName, false);
        return caseSensitivePath;
    }

    /// <inheritdoc />
    public string PrefixWithHostDirectory(string dosPath) {
        string path = dosPath;

        if (string.IsNullOrWhiteSpace(path)) {
            return path;
        }

        if (IsPathRooted(path)) {
            int length = 1;
            if(StartsWithDosDrive(path)) {
                length = 3;
            }
            path = Path.Combine(DriveMap[path[0]].FullName,path[length..]);
        } else if (StartsWithDosDrive(dosPath)) {
            path = Path.Combine(CurrentHostDirectory, path[2..]);
        } else {
            path = Path.Combine(CurrentHostDirectory, path);
        }

        return ConvertUtils.ToSlashPath(path);
    }

    /// <summary>
    /// All the possible DOS drive letters
    /// </summary>
    private static IEnumerable<char> DriveLetters => "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    private bool StartsWithDosDrive(string path) =>
        path.Length >= 2 &&
        DriveLetters.Contains(char.ToUpperInvariant(path[0])) &&
        path[1] == ':';

    private bool IsPathRooted(string path) =>
        path.StartsWith(@"\") ||
        path.Length >= 3 &&
        StartsWithDosDrive(path) &&
        (path[2] == '\\' || path[2] == '/');

    /// <inheritdoc />
    public bool AnyDosDirectoryOrFileWithTheSameName(string newFileOrDirectoryPath, DirectoryInfo hostFolder) =>
        GetTopLevelDirsAndFiles(hostFolder.FullName).Any(x => string.Equals(x, newFileOrDirectoryPath, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<string> GetTopLevelDirsAndFiles(string hostPath, string searchPattern = "*") {
        return Directory
            .GetDirectories(hostPath, searchPattern)
            .Concat(Directory.GetFiles(hostPath, searchPattern));
    }
}