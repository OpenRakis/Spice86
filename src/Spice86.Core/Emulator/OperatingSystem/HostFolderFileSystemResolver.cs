namespace Spice86.Core.Emulator.OperatingSystem;

using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Interfaces;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System.Linq;
using System.Text;

/// <summary>
/// Host folder implementation of DOS path resolution.
/// This class implements the existing <see cref="IDosPathResolver"/> contract for <see cref="HostFolderDrive"/>
/// </summary>
public class HostFolderFileSystemResolver : IDosPathResolver {
    internal const char VolumeSeparatorChar = ':';
    internal const char DirectorySeparatorChar = '\\';
    internal const char AltDirectorySeparatorChar = '/';
    private const int MaxPathLength = 255;
    private const int DosMfnlength = 8;
    private const int DosExtlength = 3;
    private const int LfnNamelength = 255; // for the sanity check only

    private readonly HostFolderDrive _drive;
    private readonly ILoggerService _loggerService;
    private readonly Func<DosDriveManager> _getDriveManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="HostFolderFileSystemResolver"/> class.
    /// </summary>
    /// <param name="drive">The host folder drive this resolver is for</param>
    /// <param name="loggerService">The logger service</param>
    /// <param name="getDriveManager">Function to get the drive manager (to avoid circular dependency)</param>
    public HostFolderFileSystemResolver(HostFolderDrive drive, ILoggerService loggerService, Func<DosDriveManager> getDriveManager) {
        _drive = drive;
        _loggerService = loggerService;
        _getDriveManager = getDriveManager;
    }

    /// <inheritdoc />
    public DosFileOperationResult GetCurrentDosDirectory(byte driveNumber, out string currentDir) {
        DosDriveManager driveManager = _getDriveManager();

        //0 = default drive
        if (driveNumber == 0 && driveManager.Count > 0) {
            (HostFolderDrive drive, IDosPathResolver _) = driveManager.CurrentDriveEntry;
            currentDir = drive.CurrentDosDirectory;
            return DosFileOperationResult.NoValue();
        } else {
            char driveLetter = DosDriveManager.DriveLetters.Keys.ElementAtOrDefault(driveNumber - 1);
            if (driveManager.TryGetDriveEntry(driveLetter, out (HostFolderDrive drive, IDosPathResolver resolver) driveEntry)) {
                currentDir = driveEntry.drive.CurrentDosDirectory;
                return DosFileOperationResult.NoValue();
            }
        }
        currentDir = "";
        return DosFileOperationResult.Error(DosErrorCode.InvalidDrive);
    }

    /// <inheritdoc />
    public DosFileOperationResult SetCurrentDir(string dosPath) {
        string fullDosPath = GetFullDosPathIncludingRoot(dosPath);

        if (!StartsWithDosDriveAndVolumeSeparator(fullDosPath)) {
            return DosFileOperationResult.Error(DosErrorCode.PathNotFound);
        }

        string? hostPath = GetFullHostPathFromDosOrDefault(fullDosPath);
        if (!string.IsNullOrWhiteSpace(hostPath)) {
            return SetCurrentDirValue(fullDosPath[0], hostPath, fullDosPath);
        } else {
            return DosFileOperationResult.Error(DosErrorCode.PathNotFound);
        }
    }

    /// <inheritdoc />
    public string? GetFullHostParentPathFromDosOrDefault(string dosPath) {
        string? parentPath = Path.GetDirectoryName(dosPath);
        if (string.IsNullOrWhiteSpace(parentPath)) {
            DosDriveManager driveManager = _getDriveManager();
            (HostFolderDrive drive, IDosPathResolver _) = driveManager.CurrentDriveEntry;
            parentPath = GetFullCurrentDosPathOnDrive(drive);
        }
        string? fullHostPath = GetFullHostPathFromDosOrDefault(parentPath);
        if (string.IsNullOrWhiteSpace(fullHostPath)) {
            return null;
        }
        return ConvertUtils.ToSlashFolderPath(fullHostPath);
    }

    /// <inheritdoc />
    public string? GetFullHostPathFromDosOrDefault(string dosPath) {
        if (string.IsNullOrWhiteSpace(dosPath)) {
            return null;
        }
        dosPath = GetFullDosPathIncludingRoot(dosPath);

        (string hostPrefix, string dosRelativePath) = DeconstructDosPath(dosPath);

        if (string.IsNullOrWhiteSpace(dosRelativePath)) {
            return ConvertUtils.ToSlashPath(hostPrefix);
        }

        string slashedRelative = ConvertUtils.ToSlashPath(dosRelativePath);
        int lastSlash = slashedRelative.LastIndexOf('/');
        string dirPart = lastSlash >= 0 ? slashedRelative[..lastSlash] : string.Empty;
        string lastSegment = lastSlash >= 0 ? slashedRelative[(lastSlash + 1)..] : slashedRelative;

        string? resolvedHostDir = ResolveCaseInsensitiveDirectory(hostPrefix, dirPart);
        if (string.IsNullOrWhiteSpace(resolvedHostDir)) {
            return null;
        }

        if (string.IsNullOrWhiteSpace(lastSegment)) {
            return ConvertUtils.ToSlashPath(resolvedHostDir);
        }

        var options = new EnumerationOptions {
            RecurseSubdirectories = false,
            MatchCasing = MatchCasing.CaseInsensitive,
            ReturnSpecialDirectories = false
        };

        string? firstMatch = FindFilesUsingWildCmp(resolvedHostDir, lastSegment, options).FirstOrDefault();
        return string.IsNullOrWhiteSpace(firstMatch) ? null : ConvertUtils.ToSlashPath(firstMatch);
    }

    /// <inheritdoc />
    public string PrefixWithHostDirectory(string dosPath) {
        if (string.IsNullOrWhiteSpace(dosPath)) {
            return dosPath;
        }
        dosPath = GetFullDosPathIncludingRoot(dosPath);
        (string HostPrefix, string DosRelativePath) = DeconstructDosPath(dosPath);
        return ConvertUtils.ToSlashPath(Path.Combine(HostPrefix, DosRelativePath));
    }

    /// <inheritdoc />
    public bool AnyDosDirectoryOrFileWithTheSameName(string newFileOrDirectoryPath, DirectoryInfo hostFolder) =>
        GetTopLevelDirsAndFiles(hostFolder.FullName).Any(x => string.Equals(x, newFileOrDirectoryPath, StringComparison.OrdinalIgnoreCase));

    /// <inheritdoc />
    public IEnumerable<string> FindFilesUsingWildCmp(string searchFolder, string searchPattern, EnumerationOptions enumerationOptions) {
        return Directory.EnumerateFileSystemEntries(searchFolder, "*", enumerationOptions)
            .Where(path => WildFileCmp(Path.GetFileName(path), searchPattern));
    }

    // Internal static methods from original DosPathResolver
    internal static string GetExeParentFolder(string? exe) {
        string fallbackValue = ConvertUtils.ToSlashFolderPath(Environment.CurrentDirectory);
        if (string.IsNullOrWhiteSpace(exe)) {
            return fallbackValue;
        }
        string? parent = Path.GetDirectoryName(exe);
        return string.IsNullOrWhiteSpace(parent) ? fallbackValue : ConvertUtils.ToSlashFolderPath(parent);
    }

    internal static string GetShortFileName(string hostFileName, string hostDir) {
        string fileName = Path.GetFileNameWithoutExtension(hostFileName);
        string extension = Path.GetExtension(hostFileName);
        // Initialize the StringBuilder for our result
        StringBuilder shortName = new StringBuilder();

        // Count files with similar names for collision detection
        int count = 1;
        if (!string.IsNullOrWhiteSpace(hostDir) && Directory.Exists(hostDir)) {
            count = new DirectoryInfo(hostDir).EnumerateFiles($"{fileName}.*")
                .TakeWhile(x => x.Name != hostFileName).Count() + 1;
        }

        // Handle filename part (8 characters)
        if (fileName.Length > 8) {
            // Need to add tilde notation (~N)
            int digitsInCount = count.ToString().Length;
            int charsToKeep = Math.Max(1, 8 - 1 - digitsInCount);

            shortName.Append(fileName.AsSpan(0, charsToKeep));
            shortName.Append('~');
            shortName.Append(count);
        } else {
            shortName.Append(fileName);
        }

        if (extension != null) {
            if (extension.Length > 4) {
                shortName.Append(extension.AsSpan(0, 4));
            } else {
                shortName.Append(extension);
            }
        }
        return shortName.ToString().ToUpperInvariant();
    }

    // Private implementation methods (copied from original DosPathResolver)
    private static string GetFullCurrentDosPathOnDrive(HostFolderDrive virtualDrive) =>
        Path.Combine($"{virtualDrive.DosVolume}{DirectorySeparatorChar}", virtualDrive.CurrentDosDirectory);

    private static bool IsWithinMountPoint(string hostFullPath, HostFolderDrive virtualDrive) =>
        hostFullPath.StartsWith(virtualDrive.MountedHostDirectory);

    private DosFileOperationResult SetCurrentDirValue(char driveLetter, string? hostFullPath, string fullDosPath) {
        DosDriveManager driveManager = _getDriveManager();

        if (!driveManager.TryGetDriveEntry(driveLetter, out (HostFolderDrive drive, IDosPathResolver resolver) driveEntry)) {
            return DosFileOperationResult.Error(DosErrorCode.PathNotFound);
        }

        if (string.IsNullOrWhiteSpace(hostFullPath) ||
            !IsWithinMountPoint(hostFullPath, driveEntry.drive) ||
            Encoding.ASCII.GetByteCount(fullDosPath) > MaxPathLength) {
            return DosFileOperationResult.Error(DosErrorCode.PathNotFound);
        }

        driveEntry.drive.CurrentDosDirectory = fullDosPath[3..];
        return DosFileOperationResult.NoValue();
    }

    private string GetDosDrivePathFromDosPath(string absoluteOrRelativeDosPath) {
        if (IsPathRooted(absoluteOrRelativeDosPath)) {
            if (StartsWithDosDriveAndVolumeSeparator(absoluteOrRelativeDosPath)) {
                return $"{absoluteOrRelativeDosPath[0]}{VolumeSeparatorChar}";
            }
        }
        return _drive.DosVolume;
    }

    private string GetFullDosPathIncludingRoot(string absoluteOrRelativeDosPath) {
        if (string.IsNullOrWhiteSpace(absoluteOrRelativeDosPath)) {
            return absoluteOrRelativeDosPath;
        }
        StringBuilder normalizedDosPath = new();

        string backslashedDosPath = ConvertUtils.ToBackSlashPath(absoluteOrRelativeDosPath);

        string driveRoot = $"{GetDosDrivePathFromDosPath(backslashedDosPath)}{DirectorySeparatorChar}";
        normalizedDosPath.Append(driveRoot);

        if (backslashedDosPath.StartsWith(driveRoot)) {
            backslashedDosPath = backslashedDosPath[3..];
        } else if (backslashedDosPath.StartsWith(driveRoot[..2])) {
            backslashedDosPath = backslashedDosPath[2..];
        }

        IEnumerable<string> pathElements = backslashedDosPath.Split(DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        bool moveNext = false;
        bool appendedFolder = false;
        bool mustPrependDirectorySeparator = false;
        foreach (string pathElement in pathElements) {
            if (pathElement == ".." && appendedFolder) {
                moveNext = true;
            } else {
                if (moveNext) {
                    moveNext = false;
                    continue;
                }
                if (pathElement != "." && pathElement != ".." && !pathElement.Contains(VolumeSeparatorChar)) {
                    appendedFolder = true;
                    if (mustPrependDirectorySeparator) {
                        normalizedDosPath.Append(DirectorySeparatorChar);
                    }
                    normalizedDosPath.Append(pathElement.ToUpperInvariant());
                    mustPrependDirectorySeparator = true;
                }
            }
        }

        return ConvertUtils.ToBackSlashPath(normalizedDosPath.ToString());
    }

    private (string hostPrefixPath, string dosRelativePath) DeconstructDosPath(string dosPath) {
        DosDriveManager driveManager = _getDriveManager();

        if (IsPathRooted(dosPath)) {
            int length = 1;
            if (StartsWithDosDriveAndVolumeSeparator(dosPath)) {
                length = 3;
            }
            return (_drive.MountedHostDirectory, dosPath[length..]);
        }

        return StartsWithDosDriveAndVolumeSeparator(dosPath)
            ? (driveManager.GetDriveEntry(dosPath[0]).drive.MountedHostDirectory, dosPath[2..])
            : (_drive.MountedHostDirectory, dosPath);
    }

    private static string? ResolveCaseInsensitiveDirectory(string hostPrefix, string dirPart) {
        if (string.IsNullOrWhiteSpace(dirPart)) {
            return hostPrefix;
        }

        string current = hostPrefix;
        foreach (string seg in dirPart.Split('/',
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
            var di = new DirectoryInfo(current);
            DirectoryInfo? next = di
                .EnumerateDirectories("*", new EnumerationOptions {
                    RecurseSubdirectories = false,
                    MatchCasing = MatchCasing.CaseInsensitive
                })
                .FirstOrDefault(d => string.Equals(d.Name, seg, StringComparison.OrdinalIgnoreCase));

            if (next == null) {
                return null;
            }

            current = next.FullName;
        }

        return current;
    }

    private bool StartsWithDosDriveAndVolumeSeparator(string dosPath) =>
        dosPath.Length >= 2 &&
        DosDriveManager.DriveLetters.Keys.Contains(char.ToUpperInvariant(dosPath[0])) &&
        dosPath[1] == VolumeSeparatorChar;

    private bool IsPathRooted(string path) =>
        path.StartsWith(DirectorySeparatorChar) ||
        path.StartsWith(AltDirectorySeparatorChar) ||
        (path.Length >= 3 &&
        StartsWithDosDriveAndVolumeSeparator(path) &&
        path[2] == DirectorySeparatorChar);

    private static IEnumerable<string> GetTopLevelDirsAndFiles(string hostPath, string searchPattern = "*") {
        return Directory
            .GetDirectories(hostPath, searchPattern)
            .Concat(Directory.GetFiles(hostPath, searchPattern));
    }

    private static bool WildFileCmp(string? filename, string? pattern) {
        if (filename is null || pattern is null) {
            return false;
        }

        return WildFileCmp(filename.AsSpan(), pattern.AsSpan());
    }

    public static bool WildFileCmp(ReadOnlySpan<char> sourceFilename, ReadOnlySpan<char> pattern) {
        if (sourceFilename.Length > 0 && pattern.Length == 0) {
            return false;
        }

        if (pattern.Length > LfnNamelength) {
            return false;
        }

        // Fast path: exact case-insensitive match (covers common no-wildcard cases)
        if (sourceFilename.Equals(pattern, StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        // Skip "hidden" dot-files if the pattern uses wildcards (except "." / "..")
        if (WildcardMatchesHiddenFile(sourceFilename, pattern)) {
            return false;
        }

        // Uppercase once into fixed-size 8.3 stacks with space padding
        Span<char> fileName = stackalloc char[DosMfnlength];
        Span<char> fileExt = stackalloc char[DosExtlength];
        Span<char> wildName = stackalloc char[DosMfnlength];
        // wild ext needs an extra slot to check the 4th char like DOSBox
        Span<char> wildExt = stackalloc char[DosExtlength + 1];

        SplitTo83(sourceFilename, fileName, fileExt, out _);
        SplitTo83(pattern, wildName, wildExt, out int wildExtLength);

        // ---- NAME compare (no early '*' accept; '*' just ends name matching) ----
        if (CompareSegment(fileName, wildName, DosMfnlength) == false) {
            return false;
        }
        // the original code tolerated extra chars in pattern name beyond 8 unless it's a non-'*' check byte; our fixed-size truncation mirrors that behavior

        // ---- EXT compare (early '*' accept) ----
        return CompareSegment(fileExt, wildExt, DosExtlength) switch {
            true => true,
            false => false,
            // If wild ext has a 4th char, and it's not '*', reject (DOSBox-like behavior)
            _ => wildExtLength <= DosExtlength || wildExt[DosExtlength] == '*'
        };
    }

    private static bool? CompareSegment(ReadOnlySpan<char> filenameOrExt, ReadOnlySpan<char> pattern, int length) {
        for (int i = 0; i < length; i++) {
            char patternChar = pattern[i];
            if (patternChar == '*') {
                return true;
            }

            if (patternChar != '?' && patternChar != filenameOrExt[i]) {
                return false;
            }
        }

        return null;
    }

    private static void SplitTo83(ReadOnlySpan<char> file, Span<char> targetFileName, Span<char> targetFileExt,
        out int extLength) {
        targetFileName.Fill(' ');
        targetFileExt.Fill(' ');

        int dotPos = file.LastIndexOf('.');
        ReadOnlySpan<char> fileNameRaw = dotPos >= 0 ? file[..dotPos] : file;
        ReadOnlySpan<char> fileExtRaw =
            dotPos >= 0 && dotPos + 1 < file.Length ? file[(dotPos + 1)..] : ReadOnlySpan<char>.Empty;
        ToUpperCopy(fileNameRaw[..Math.Min(fileNameRaw.Length, targetFileName.Length)], targetFileName);
        ToUpperCopy(fileExtRaw[..Math.Min(fileExtRaw.Length, targetFileExt.Length)], targetFileExt);
        extLength = fileExtRaw.Length; // actual (untruncated) length for the DOSBox-style 4th-char check
    }

    private static bool WildcardMatchesHiddenFile(ReadOnlySpan<char> fileName, ReadOnlySpan<char> wildcard) {
        if (fileName.IsEmpty) {
            return false;
        }

        if (wildcard.IndexOfAny('?', '*') < 0) {
            return false;
        }

        return fileName.Length >= 5 && fileName[0] == '.' &&
               !fileName.Equals(".", StringComparison.Ordinal) &&
               !fileName.Equals("..", StringComparison.Ordinal);
    }

    private static void ToUpperCopy(ReadOnlySpan<char> src, Span<char> dst) {
        src.ToUpperInvariant(dst);
    }
}