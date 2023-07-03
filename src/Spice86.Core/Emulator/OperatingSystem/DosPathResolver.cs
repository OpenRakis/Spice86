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
    public string CurrentHostDirectory => DriveMap[CurrentDrive].FullName;

    private readonly ILoggerService _loggerService;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="loggerService">The logger service implementation.</param>
    public DosPathResolver(ILoggerService loggerService) => _loggerService = loggerService;

    /// <inheritdoc />
    public string GetHostRelativePathToCurrentDirectory(string hostPath) => Path.GetRelativePath(CurrentHostDirectory, hostPath);

    /// <inheritdoc/>
    public DosFileOperationResult SetCurrentDir(string dosPath) {
        if (IsPathRooted(dosPath)) {
            string? newCurrentDirectory = ToHostCaseSensitiveFullName(dosPath, false);
            if (!string.IsNullOrWhiteSpace(newCurrentDirectory)) {
                DriveMap[dosPath[0]].CurrentDirectory = GetHostRelativePathToCurrentDirectory(GetHostFullNameForParentDirectory(newCurrentDirectory));
                return DosFileOperationResult.NoValue();
            }
        }

        if (dosPath == "." || dosPath == @".\") {
            return DosFileOperationResult.NoValue();
        }

        if (dosPath == ".." || dosPath == @"..\") {
            DriveMap[CurrentDrive].CurrentDirectory = GetHostRelativePathToCurrentDirectory(GetHostFullNameForParentDirectory(CurrentHostDirectory));
            return DosFileOperationResult.NoValue();
        }

        while (dosPath.StartsWith("..\\")) {
            dosPath = dosPath[3..];
            DriveMap[CurrentDrive].CurrentDirectory = GetHostRelativePathToCurrentDirectory(GetHostFullNameForParentDirectory(CurrentHostDirectory));
        }

        while(dosPath.StartsWith(@"\")) {
            dosPath = dosPath[1..];
        }

        string? hostFullPath = ToHostCaseSensitiveFullName(dosPath, false);

        if(string.IsNullOrWhiteSpace(hostFullPath)) {
            return DosFileOperationResult.Error(ErrorCode.PathNotFound);
        }
        DriveMap[CurrentDrive].CurrentDirectory = GetHostRelativePathToCurrentDirectory(hostFullPath);
        return DosFileOperationResult.NoValue();
}

    /// <inheritdoc/>
    public void SetDiskParameters(char currentDrive, string dosPath, Dictionary<char, MountedFolder> driveMap) {
        DriveMap = driveMap;
        CurrentDrive = currentDrive;
        SetCurrentDir(dosPath);
    }

    /// <inheritdoc />
    public string GetHostFullNameForParentDirectory(string hostPath) => Directory.GetParent(hostPath)?.FullName ?? hostPath;



    private static string? TryGetFullNameOnDiskOfParentDirectory(string hostDirectory) {
        if (string.IsNullOrWhiteSpace(hostDirectory)) {
            return null;
        }
        DirectoryInfo hostDirectoryInfo = new(hostDirectory);
        if (hostDirectoryInfo.Exists) {
            return hostDirectory;
        }

        if (hostDirectoryInfo.Parent == null) {
            return null;
        }

        string? parent = TryGetFullNameOnDiskOfParentDirectory(hostDirectoryInfo.Parent.FullName);
        if (parent == null) {
            return null;
        }

        return new DirectoryInfo(parent).GetDirectories(hostDirectoryInfo.Name, new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive }).FirstOrDefault()?.FullName;
    }

    /// <summary>
    /// Searches for the path on disk, and returns the first matching file or directory path.
    /// </summary>
    /// <param name="dosPath">The case insensitive file path</param>
    /// <param name="convertParentOnly"></param>
    /// <returns>The absolute host file path</returns>
    private string? ToCaseSensitivePath(string? dosPath, bool convertParentOnly) {
        if (string.IsNullOrWhiteSpace(dosPath)) {
            return null;
        }

        string hostPath = ConvertUtils.ToSlashPath(dosPath);
        string? parentDir = Path.GetDirectoryName(hostPath);
        if (File.Exists(hostPath) || 
            Directory.Exists(hostPath) ||
            convertParentOnly) {
            // file exists or root reached, no need to go further. Path found.
            return dosPath;
        }

        string? parent = ToCaseSensitivePath(parentDir, convertParentOnly);
        if (parent == null) {
            // End of recursion, root reached
            return null;
        }

        // Now that parent is for sure on the host file system, let's find the actual path
        try {
            string? fileNameOnFileSystem = TryGetFullHostFileName(dosPath);
            if (!string.IsNullOrWhiteSpace(fileNameOnFileSystem)) {
                return fileNameOnFileSystem;
            }
            string searchPattern = Path.GetFileName(hostPath);
            if (Directory.Exists(parent)) {
                return
                    Directory.GetDirectories(parent, searchPattern)
                    .Concat(
                    Directory.GetFiles(parent, searchPattern))
                    .FirstOrDefault();
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
    public string? TryGetFullHostFileName(string dosFilePath) {
        string? directory = Path.GetDirectoryName(dosFilePath);
        if (string.IsNullOrWhiteSpace(directory)) {
            return null;
        }
        string? directoryCaseSensitive = TryGetFullNameOnDiskOfParentDirectory(directory);
        if (string.IsNullOrWhiteSpace(directoryCaseSensitive) || !Directory.Exists(directoryCaseSensitive)) {
            return null;
        }
        string hostFileName = "";
        IEnumerable<string> array = Directory.GetFiles(directoryCaseSensitive).Select(x => x.ToUpperInvariant());
        string searchedFile = dosFilePath.ToUpperInvariant();
        foreach (string file in array) {
            if (file == searchedFile) {
                hostFileName = file;
            }
        }
        return hostFileName;
    }

    /// <inheritdoc />
    public string? ToHostCaseSensitiveFullName(string dosPath, bool convertParentOnly) {
        string fileName = PrefixWithHostDirectory(dosPath);
        if (!convertParentOnly) {
            string? caseSensitivePath = ToCaseSensitivePath(fileName, convertParentOnly);
            return caseSensitivePath;
        }
        
        string? parent = ToCaseSensitivePath(fileName, convertParentOnly);
        if (string.IsNullOrWhiteSpace(parent)) {
            return null;
        }
        
        return ConvertUtils.ToSlashPath(parent);
    }

    /// <inheritdoc />
    public string PrefixWithHostDirectory(string dosPath) {
        string path = dosPath;

        if(string.IsNullOrWhiteSpace(path)) {
            return path;
        }

        if(IsPathRooted(path)) {
            path = Path.Combine(DriveMap[path[0]].FullName, path[3..]);
        }
        else if (StartsWithDosDrive(dosPath)) {
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
        path.Length >= 3 &&
        StartsWithDosDrive(path) &&
        (path[2] == '\\' || path[2] == '/');

    /// <inheritdoc />
    public bool AnyDirectoryOrFileWithTheSameName(string newFileOrDirectoryPath, DirectoryInfo hostFolder) =>
        hostFolder.GetDirectories().Select(x => x.Name)
            .Concat(hostFolder.GetFiles().Select(x => x.Name))
            .Any(x => string.Equals(x, newFileOrDirectoryPath, StringComparison.OrdinalIgnoreCase));
}