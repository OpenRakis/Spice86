namespace Spice86.Core.Emulator.OperatingSystem;

using Spice86.Shared.Emulator.Errors;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;

/// <inheritdoc />
public class DosFilePathResolver : IDosFilePathResolver {
    private readonly ILoggerService _loggerService;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="loggerService">The logger service implementation.</param>
    public DosFilePathResolver(ILoggerService loggerService) => _loggerService = loggerService;
    
    /// <inheritdoc />
    public string GetHostParentDirectory(string path) => Directory.GetParent(path)?.FullName ?? path;

    /// <inheritdoc />
    public string? GetActualCaseForFileName(string caseInsensitivePath) {
        string? directory = Path.GetDirectoryName(caseInsensitivePath);
        string? directoryCaseSensitive = GetDirectoryCaseSensitive(directory);
        if (string.IsNullOrWhiteSpace(directoryCaseSensitive) || Directory.Exists(directoryCaseSensitive) == false) {
            return null;
        }
        string realFileName = "";
        string[] array = Directory.GetFiles(directoryCaseSensitive);
        foreach (string file in array) {
            string fileToUpper = file.ToUpperInvariant();
            string searchedFile = caseInsensitivePath.ToUpperInvariant();
            if (fileToUpper == searchedFile) {
                realFileName = file;
            }
        }
        if (string.IsNullOrWhiteSpace(realFileName) || File.Exists(realFileName) == false) {
            return null;
        }
        return realFileName;
    }

    private static string? GetDirectoryCaseSensitive(string? directory) {
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

        string? parent = GetDirectoryCaseSensitive(directoryInfo.Parent.FullName);
        if (parent == null) {
            return null;
        }

        return new DirectoryInfo(parent).GetDirectories(directoryInfo.Name, new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive }).FirstOrDefault()?.FullName;
    }
    
    private static string ReplaceDriveWithHostPath(IDictionary<char, string> driveMap, string fileName) {
        // Absolute path
        char driveLetter = fileName.ToUpperInvariant()[0];

        if (driveMap.TryGetValue(driveLetter, out string? pathForDrive) == false) {
            throw new UnrecoverableException($"Could not find a mapping for drive {driveLetter}");
        }

        return Path.Combine(pathForDrive, fileName[3..]);
    }

    /// <summary>
    /// Searches for the path on disk, and returns the first matching file or folder path.
    /// </summary>
    /// <param name="caseInsensitivePath">The case insensitive file path</param>
    /// <returns>The absolute host file path</returns>
    private string? ToCaseSensitiveFileName(string? caseInsensitivePath) {
        if (string.IsNullOrWhiteSpace(caseInsensitivePath)) {
            return null;
        }

        string fileToProcess = ConvertUtils.ToSlashPath(caseInsensitivePath);
        string? parentDir = Path.GetDirectoryName(fileToProcess);
        if (File.Exists(fileToProcess) || Directory.Exists(fileToProcess) ||
            (string.IsNullOrWhiteSpace(parentDir) == false && Directory.Exists(parentDir) && Directory.GetDirectories(parentDir).Length == 0)) {
            // file exists or root reached, no need to go further. Path found.
            return caseInsensitivePath;
        }

        string? parent = ToCaseSensitiveFileName(parentDir);
        if (parent == null) {
            // End of recursion, root reached
            return null;
        }

        // Now that parent is for sure on the disk, let's find the current file
        try {
            string? fileNameOnFileSystem = GetActualCaseForFileName(caseInsensitivePath);
            if (string.IsNullOrWhiteSpace(fileNameOnFileSystem) == false) {
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
    public string? ToHostCaseSensitiveFileName(IDictionary<char, string> driveMap, string hostDirectory, string dosFileName, bool forCreation) {
        string fileName = ToHostFilePath(driveMap, hostDirectory, dosFileName);
        if (!forCreation) {
            string? caseSensitivePath = ToCaseSensitiveFileName(fileName);
            return caseSensitivePath;
        }
        
        string? parent = ToCaseSensitiveFileName(fileName);
        if (string.IsNullOrWhiteSpace(parent)) {
            return null;
        }
        
        // Concat the folder to the requested file name
        return ConvertUtils.ToSlashPath(parent);
    }

    /// <inheritdoc />
    public string ToHostFilePath(IDictionary<char, string> driveMap, string hostDirectory, string dosFileName) {
        string fileName = ConvertUtils.ToSlashPath(dosFileName);
        if (IsDosPathRooted(fileName)) {
            fileName = ReplaceDriveWithHostPath(driveMap, fileName);
        } else if (string.IsNullOrWhiteSpace(hostDirectory) == false) {
            fileName = Path.Combine(hostDirectory, fileName);
        }

        return ConvertUtils.ToSlashPath(fileName);
    }

    /// <summary>
    /// All the possible DOS drive letters
    /// </summary>
    private static string[] DriveLetters { get; } = {"A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z" };
    
    /// <inheritdoc />
    public bool IsDosPathRooted(string path) => path.Length >= 2 && DriveLetters.Contains(path[0].ToString().ToUpperInvariant()) && path[1] == ':';
}