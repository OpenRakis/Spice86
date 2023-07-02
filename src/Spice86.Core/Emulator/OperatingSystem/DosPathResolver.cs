namespace Spice86.Core.Emulator.OperatingSystem;

using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

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
    public string CurrentHostDirectory {
        get => DriveMap[CurrentDrive].CurrentFolder;
        private set => DriveMap[CurrentDrive].CurrentFolder = value;
    }

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
                CurrentHostDirectory = newCurrentDirectory;
                return DosFileOperationResult.NoValue();
            }
        }

        if (dosPath == ".") {
            return DosFileOperationResult.NoValue();
        }

        if (dosPath == "..") {
            CurrentHostDirectory = GetFullNameForParentDirectory(CurrentHostDirectory);
            return DosFileOperationResult.NoValue();
        }

        while (dosPath.StartsWith("..\\")) {
            dosPath = dosPath[3..];
            CurrentHostDirectory = GetFullNameForParentDirectory(CurrentHostDirectory);
        }

        CurrentHostDirectory = Path.GetFullPath(Path.Combine(CurrentHostDirectory, dosPath));
        return DosFileOperationResult.NoValue();
    }

    /// <inheritdoc/>
    public void SetDiskParameters(char currentDrive, string dosPath, Dictionary<char, MountedFolder> driveMap) {
        DriveMap = driveMap;
        CurrentDrive = currentDrive;
        SetCurrentDir(dosPath);
    }

    /// <inheritdoc />
    public string GetFullNameForParentDirectory(string dosOrHostPath) => Directory.GetParent(dosOrHostPath)?.FullName ?? dosOrHostPath;

    /// <inheritdoc />
    public string? TryGetFullHostFileName(string dosFilePath) {
        string? directory = Path.GetDirectoryName(dosFilePath);
        if(string.IsNullOrWhiteSpace(directory)) {
            return null;
        }
        string? directoryCaseSensitive = TryGetFullNameOnDiskOfParentDirectory(directory);
        if (string.IsNullOrWhiteSpace(directoryCaseSensitive) || !Directory.Exists(directoryCaseSensitive)) {
            return null;
        }
        string hostFileName = "";
        string[] array = Directory.GetFiles(directoryCaseSensitive);
        foreach (string file in array) {
            string fileToUpper = file.ToUpperInvariant();
            string searchedFile = dosFilePath.ToUpperInvariant();
            if (fileToUpper == searchedFile) {
                hostFileName = file;
            }
        }
        return hostFileName;
    }

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
    /// Searches for the path on disk, and returns the first matching file or folder path.
    /// </summary>
    /// <param name="dosPath">The case insensitive file path</param>
    /// <param name="convertParentOnly"></param>
    /// <returns>The absolute host file path</returns>
    private string? ToCaseSensitiveFileName(string? dosPath, bool convertParentOnly) {
        if (string.IsNullOrWhiteSpace(dosPath)) {
            return null;
        }

        string fileToProcess = ConvertUtils.ToSlashPath(dosPath);
        string? parentDir = Path.GetDirectoryName(fileToProcess);
        if (File.Exists(fileToProcess) || 
            Directory.Exists(fileToProcess) ||
            convertParentOnly) {
            // file exists or root reached, no need to go further. Path found.
            return dosPath;
        }

        string? parent = ToCaseSensitiveFileName(parentDir, convertParentOnly);
        if (parent == null) {
            // End of recursion, root reached
            return null;
        }

        // Now that parent is for sure on the disk, let's find the current file
        try {
            string? fileNameOnFileSystem = TryGetFullHostFileName(dosPath);
            if (!string.IsNullOrWhiteSpace(fileNameOnFileSystem)) {
                return fileNameOnFileSystem;
            }
            Regex fileToProcessRegex = FileSpecToRegex(Path.GetFileName(fileToProcess));
            if (Directory.Exists(parent)) {
                return Array.Find(Directory.GetFiles(parent), 
                    x => fileToProcessRegex.IsMatch(x));
            }
        } catch (IOException e) {
            e.Demystify();
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
                _loggerService.Warning(e, "Error while checking file {CaseInsensitivePath}: {Exception}", dosPath, e);
            }
        }

        return null;
    }
    
    /// <summary>
    /// Converts a dosVirtualDevices filespec to a regex pattern
    /// </summary>
    /// <param name="fileSpec">The DOS filespec</param>
    /// <returns>The regex pattern</returns>
    private static Regex FileSpecToRegex(string fileSpec) {
        string regex = fileSpec.ToLowerInvariant();
        regex = regex.Replace(".", "[.]");
        regex = regex.Replace("?", ".");
        regex = regex.Replace("*", ".*");
        return new Regex(regex);
    }
    
    /// <inheritdoc />
    public string? ToHostCaseSensitiveFullName(string dosPath, bool convertParentOnly) {
        string fileName = PrefixWithHostDirectory(dosPath);
        if (!convertParentOnly) {
            string? caseSensitivePath = ToCaseSensitiveFileName(fileName, convertParentOnly);
            return caseSensitivePath;
        }
        
        string? parent = ToCaseSensitiveFileName(fileName, convertParentOnly);
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
    private static string[] DriveLetters { get; } = { "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z" };

    private bool StartsWithDosDrive(string path) => 
        path.Length >= 2 &&
        DriveLetters.Contains(path[0].ToString().ToUpperInvariant()) &&
        path[1] == ':';

    private bool IsPathRooted(string path) => 
        path.Length >= 3 &&
        StartsWithDosDrive(path) &&
        (path[2] == '\\' || path[2] == '/');

    /// <inheritdoc />
    public bool IsThereAnyDirectoryOrFileWithTheSameName(string newFileOrFolderName, DirectoryInfo hostFolder) =>
        hostFolder.GetDirectories().Select(x => x.Name)
            .Concat(hostFolder.GetFiles().Select(x => x.Name))
            .Any(x => string.Equals(x, newFileOrFolderName, StringComparison.OrdinalIgnoreCase));
}