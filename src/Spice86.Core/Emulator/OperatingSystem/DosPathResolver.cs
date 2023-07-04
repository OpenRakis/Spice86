namespace Spice86.Core.Emulator.OperatingSystem;

using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

/// <inheritdoc cref="IDosPathResolver" />
public class DosPathResolver : IDosPathResolver {

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


    private readonly ILoggerService _loggerService;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="loggerService">The logger service implementation.</param>
    /// <param name="configuration">The emulator configuration.</param>
    public DosPathResolver(ILoggerService loggerService, Configuration configuration) {
        _loggerService = loggerService;
        DriveMap = InitializeDriveMap(configuration);
        CurrentDrive = 'C';
        SetCurrentDir(@"C:\");
    }

    private static string GetExeParentFolder(Configuration configuration) {
        string? exe = configuration.Exe;
        if (string.IsNullOrWhiteSpace(exe)) {
            return Environment.CurrentDirectory;
        }

        DirectoryInfo? parentDir = Directory.GetParent(exe);
        // Must be in the current directory
        parentDir ??= new DirectoryInfo(Environment.CurrentDirectory);

        string parent = Path.GetFullPath(parentDir.FullName);
        return ConvertUtils.ToSlashFolderPath(parent);
    }

    private IDictionary<char, MountedFolder> InitializeDriveMap(Configuration configuration) {
        string parentFolder = GetExeParentFolder(configuration);
        Dictionary<char, MountedFolder> driveMap = new();
        string? cDrive = configuration.CDrive;
        if (string.IsNullOrWhiteSpace(cDrive)) {
            cDrive = parentFolder;
        }
        cDrive = ConvertUtils.ToSlashFolderPath(cDrive);
        driveMap.Add('C', new MountedFolder(cDrive));
        return driveMap;
    }

    /// <inheritdoc />
    public string GetHostRelativePathToCurrentDirectory(string hostPath) => Path.GetRelativePath(CurrentHostDirectory, hostPath);

    private static bool IsWithinMountPoint(string hostFullPath, MountedFolder mountedFolder) =>
        hostFullPath.StartsWith(mountedFolder.MountPoint);

    /// <inheritdoc/>
    public DosFileOperationResult SetCurrentDir(string dosPath) {
        if (IsPathRooted(dosPath)) {
            string? hostPath = TryGetFullHostPath(dosPath);
            if (!string.IsNullOrWhiteSpace(hostPath)) {
                char driveLetter = StartsWithDosDrive(dosPath) ? dosPath[0] : CurrentDrive;
                return SetCurrentDirValue(driveLetter, GetHostRelativePathToCurrentDirectory(hostPath));
            } else {
                return DosFileOperationResult.Error(ErrorCode.PathNotFound);
            }
        }

        if (dosPath == "." || dosPath == @".\") {
            return DosFileOperationResult.NoValue();
        }

        if (dosPath == ".." || dosPath == @"..\") {
            string newCurrentDir = GetHostFullNameForParentDirectory(CurrentHostDirectory);
            return SetCurrentDirValue(CurrentDrive, newCurrentDir);
        }

        while (dosPath.StartsWith("..\\")) {
            dosPath = dosPath[3..];
            string newCurrentDir = GetHostFullNameForParentDirectory(CurrentHostDirectory);
            SetCurrentDirValue(CurrentDrive, newCurrentDir);
        }

        string? hostFullPath = TryGetFullHostPath(dosPath);
        return SetCurrentDirValue(CurrentDrive, hostFullPath);
    }

    private DosFileOperationResult SetCurrentDirValue(char driveLetter, string? hostFullPath) {
        if (string.IsNullOrWhiteSpace(hostFullPath) ||
            !IsWithinMountPoint(hostFullPath, DriveMap[CurrentDrive]) ||
            Encoding.ASCII.GetByteCount(hostFullPath) > 255) {
            return DosFileOperationResult.Error(ErrorCode.PathNotFound);
        }

        DriveMap[driveLetter].CurrentDirectory = hostFullPath;
        return DosFileOperationResult.NoValue();
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
        if (string.IsNullOrWhiteSpace(dosPath)) {
            return dosPath;
        }

        string path;
        if (IsPathRooted(dosPath)) {
            int length = 1;
            if (StartsWithDosDrive(dosPath)) {
                length = 3;
            }
            path = Path.Combine(DriveMap[dosPath[0]].MountPoint, dosPath[length..]);
        } else if (StartsWithDosDrive(dosPath)) {
            path = Path.Combine(DriveMap[dosPath[0]].MountPoint, dosPath[2..]);
        } else {
            path = Path.Combine(DriveMap[CurrentDrive].MountPoint, dosPath);
        }

        return ConvertUtils.ToSlashPath(path);
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