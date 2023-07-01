namespace Spice86.Core.Emulator.OperatingSystem;

using Spice86.Shared.Emulator.Errors;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System.Diagnostics;
using System.IO;
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
    
    private static string ReplaceDriveWithHostPath(IDictionary<char, string> driveMap, string fileName) {
        // Absolute path
        char driveLetter = fileName.ToUpperInvariant()[0];

        if (!driveMap.TryGetValue(driveLetter, out string? pathForDrive)) {
            throw new UnrecoverableException($"Could not find a mapping for drive {driveLetter}");
        }

        fileName = fileName[2..];

        // Path.Combine won't combine if the filename begins with a slash.
        // Fixes games asking for a rooted file name (example: 'C:/DUNE2.EXE')
        while (fileName.StartsWith('/')) {
            fileName = fileName[1..];
        }

        return Path.Combine(pathForDrive,  fileName);
    }

    /// <summary>
    /// Searches for the path on disk, and returns the first matching file or folder path.
    /// </summary>
    /// <param name="caseInsensitivePath">The case insensitive file path</param>
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
    public string? ToHostCaseSensitiveFullName(IDictionary<char, string> driveMap, string hostDirectory, string dosFileName, bool forCreation) {
        string fileName = PrefixWithHostDirectory(driveMap, hostDirectory, dosFileName);
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
    public string PrefixWithHostDirectory(IDictionary<char, string> driveMap, string hostDirectory, string dosPath) {
        string fileName = ConvertUtils.ToSlashPath(dosPath);
        if (IsDosPathRooted(fileName)) {
            fileName = ReplaceDriveWithHostPath(driveMap, fileName);
        } else if (!string.IsNullOrWhiteSpace(hostDirectory)) {
            fileName = Path.Combine(hostDirectory, fileName);
        }

        return ConvertUtils.ToSlashPath(fileName);
    }

    /// <summary>
    /// All the possible DOS drive letters
    /// </summary>
    private static string[] DriveLetters { get; } = {"A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z" };
    
    /// <inheritdoc />
    public bool IsDosPathRooted(string dosPath) => dosPath.Length >= 2 && DriveLetters.Contains(dosPath[0].ToString().ToUpperInvariant()) && dosPath[1] == ':';

    /// <inheritdoc />
    public bool IsThereAnyDirectoryOrFileWithTheSameName(string newFileOrFolderName, DirectoryInfo hostFolder) =>
        hostFolder.GetDirectories().Select(x => x.Name)
            .Concat(hostFolder.GetFiles().Select(x => x.Name))
            .Any(x => string.Equals(x, newFileOrFolderName, StringComparison.InvariantCultureIgnoreCase));
}