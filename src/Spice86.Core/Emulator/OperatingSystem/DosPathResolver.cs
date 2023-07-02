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
    public string GetHostRelativePathToCurrentDirectory(string path) => Path.GetRelativePath(CurrentHostDirectory, path);

    /// <inheritdoc/>
    public DosFileOperationResult SetCurrentDir(string newCurrentDir) {
        if (IsPathRooted(newCurrentDir)) {
            string? newCurrentDirectory = ToHostCaseSensitiveFullName(newCurrentDir, false);
            if (!string.IsNullOrWhiteSpace(newCurrentDirectory)) {
                CurrentHostDirectory = newCurrentDirectory;
                return DosFileOperationResult.NoValue();
            }
        }

        if (newCurrentDir == ".") {
            return DosFileOperationResult.NoValue();
        }

        if (newCurrentDir == "..") {
            CurrentHostDirectory = GetFullNameForParentDirectory(CurrentHostDirectory);
            return DosFileOperationResult.NoValue();
        }

        while (newCurrentDir.StartsWith("..\\")) {
            newCurrentDir = newCurrentDir[3..];
            CurrentHostDirectory = GetFullNameForParentDirectory(CurrentHostDirectory);
        }

        CurrentHostDirectory = Path.GetFullPath(Path.Combine(CurrentHostDirectory, newCurrentDir));
        return DosFileOperationResult.NoValue();
    }

    /// <inheritdoc/>
    public void SetDiskParameters(char currentDrive, string newCurrentDir, Dictionary<char, MountedFolder> driveMap) {
        DriveMap = driveMap;
        CurrentDrive = currentDrive;
        SetCurrentDir(newCurrentDir);
    }

    /// <inheritdoc />
    public string GetFullNameForParentDirectory(string path) => Directory.GetParent(path)?.FullName ?? path;

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
        string realFileName = "";
        string[] array = Directory.GetFiles(directoryCaseSensitive);
        foreach (string file in array) {
            string fileToUpper = file.ToUpperInvariant();
            string searchedFile = dosFilePath.ToUpperInvariant();
            if (fileToUpper == searchedFile) {
                realFileName = file;
            }
        }
        return realFileName;
    }

    private static string? TryGetFullNameOnDiskOfParentDirectory(string directory) {
        if (string.IsNullOrWhiteSpace(directory)) {
            return null;
        }
        DirectoryInfo directoryInfo = new(directory);
        if (directoryInfo.Exists) {
            return directory;
        }

        if (directoryInfo.Parent == null) {
            return null;
        }

        string? parent = TryGetFullNameOnDiskOfParentDirectory(directoryInfo.Parent.FullName);
        if (parent == null) {
            return null;
        }

        return new DirectoryInfo(parent).GetDirectories(directoryInfo.Name, new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive }).FirstOrDefault()?.FullName;
    }

    /// <summary>
    /// Searches for the path on disk, and returns the first matching file or folder path.
    /// </summary>
    /// <param name="caseInsensitivePath">The case insensitive file path</param>
    /// <param name="forCreation"></param>
    /// <returns>The absolute host file path</returns>
    private string? ToCaseSensitiveFileName(string? caseInsensitivePath, bool forCreation) {
        if (string.IsNullOrWhiteSpace(caseInsensitivePath)) {
            return null;
        }

        string fileToProcess = ConvertUtils.ToSlashPath(caseInsensitivePath);
        string? parentDir = Path.GetDirectoryName(fileToProcess);
        if (File.Exists(fileToProcess) || 
            Directory.Exists(fileToProcess) ||
            forCreation) {
            // file exists or root reached, no need to go further. Path found.
            return caseInsensitivePath;
        }

        string? parent = ToCaseSensitiveFileName(parentDir, forCreation);
        if (parent == null) {
            // End of recursion, root reached
            return null;
        }

        // Now that parent is for sure on the disk, let's find the current file
        try {
            string? fileNameOnFileSystem = TryGetFullHostFileName(caseInsensitivePath);
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
                _loggerService.Warning(e, "Error while checking file {CaseInsensitivePath}: {Exception}", caseInsensitivePath, e);
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
    public string? ToHostCaseSensitiveFullName(string dosFileName, bool forCreation) {
        string fileName = PrefixWithHostDirectory(dosFileName);
        if (!forCreation) {
            string? caseSensitivePath = ToCaseSensitiveFileName(fileName, forCreation);
            return caseSensitivePath;
        }
        
        string? parent = ToCaseSensitiveFileName(fileName, forCreation);
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
            .Any(x => string.Equals(x, newFileOrFolderName, StringComparison.InvariantCultureIgnoreCase));
}