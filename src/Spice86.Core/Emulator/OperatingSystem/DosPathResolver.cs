namespace Spice86.Core.Emulator.OperatingSystem;

using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Utils;

using System.Collections.Generic;
using System.Linq;
using System.Text;

/// <summary>
/// Translates DOS filepaths to host file paths, and vice-versa.
/// </summary>
internal class DosPathResolver {
    internal const char VolumeSeparatorChar = ':';
    internal const char DirectorySeparatorChar = '\\';
    internal const char AltDirectorySeparatorChar = '/';
    private const int MaxPathLength = 255;
    private const int DosMfnlength = 8;
    private const int DosExtlength = 3;
    private const int LfnNamelength = 255; // for the sanity check only
    // Match DOS COMMAND.COM batch-first executable lookup order: .BAT is searched before .COM and .EXE.
    private static readonly string[] ExecutableExtensionLookupOrder = [".BAT", ".COM", ".EXE"];

    private readonly DosDriveManager _dosDriveManager;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="dosDriveManager">The shared class to get all mounted DOS drives.</param>
    public DosPathResolver(DosDriveManager dosDriveManager) {
        _dosDriveManager = dosDriveManager;
    }

    /// <summary>
    /// Gets the current DOS directory.
    /// </summary>
    public DosFileOperationResult GetCurrentDosDirectory(byte driveNumber, out string currentDir) {
        //0 = default drive
        if (driveNumber == 0 && _dosDriveManager.Count > 0) {
            VirtualDrive virtualDrive = _dosDriveManager.CurrentDrive;
            currentDir = virtualDrive.CurrentDosDirectory;
            return DosFileOperationResult.NoValue();
        } else {
            char driveLetter = DosDriveManager.DriveLetters.Keys.ElementAtOrDefault(driveNumber - 1);
            if (_dosDriveManager.TryGetValue(driveLetter,
                        out VirtualDrive? virtualDrive)) {
                currentDir = virtualDrive.CurrentDosDirectory;
                return DosFileOperationResult.NoValue();
            }
        }
        currentDir = "";
        return DosFileOperationResult.Error(DosErrorCode.InvalidDrive);
    }

    private static string GetFullCurrentDosPathOnDrive(VirtualDrive virtualDrive) =>
        Path.Join($"{virtualDrive.DosVolume}{DirectorySeparatorChar}", virtualDrive.CurrentDosDirectory);

    internal static string GetExeParentFolder(string? exe) {
        string fallbackValue = ConvertUtils.ToSlashFolderPath(Environment.CurrentDirectory);
        if (string.IsNullOrWhiteSpace(exe)) {
            return fallbackValue;
        }
        string? parent = Path.GetDirectoryName(exe);
        return string.IsNullOrWhiteSpace(parent) ? fallbackValue : ConvertUtils.ToSlashFolderPath(parent);
    }

    private static bool IsWithinMountPoint(string hostFullPath, VirtualDrive virtualDrive) => hostFullPath.StartsWith(virtualDrive.MountedHostDirectory);

    /// <summary>
    /// Sets the current DOS folder.
    /// </summary>
    /// <param name="dosPath">The new DOS path to use as the current DOS folder.</param>
    /// <returns>A <see cref="DosFileOperationResult"/> that details the result of the operation.</returns>
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

    private string GetDosDrivePathFromDosPath(string absoluteOrRelativeDosPath) {
        if (IsPathRooted(absoluteOrRelativeDosPath)) {
            if (StartsWithDosDriveAndVolumeSeparator(absoluteOrRelativeDosPath)) {
                return $"{absoluteOrRelativeDosPath[0]}{VolumeSeparatorChar}";
            }
        }
        return _dosDriveManager.CurrentDrive.DosVolume;
    }

    private DosFileOperationResult SetCurrentDirValue(char driveLetter, string? hostFullPath, string fullDosPath) {
        if (string.IsNullOrWhiteSpace(hostFullPath) ||
            !IsWithinMountPoint(hostFullPath, _dosDriveManager[driveLetter]) ||
            Encoding.ASCII.GetByteCount(fullDosPath) > MaxPathLength) {
            return DosFileOperationResult.Error(DosErrorCode.PathNotFound);
        }

        _dosDriveManager[driveLetter].CurrentDosDirectory = fullDosPath[3..];
        return DosFileOperationResult.NoValue();
    }

    /// <summary>
    /// Resolves a DOS path (which may be absolute, drive-relative, or relative)
    /// to its fully-qualified form including drive root, honoring the target drive's
    /// current directory for non-rooted paths and properly applying '.' and '..' segments.
    /// </summary>
    /// <param name="absoluteOrRelativeDosPath">The DOS path to normalize.</param>
    /// <returns>A fully-qualified DOS path of the form <c>X:\PATH\TO\FILE</c>.</returns>
    private string GetFullDosPathIncludingRoot(string absoluteOrRelativeDosPath) {
        if (string.IsNullOrWhiteSpace(absoluteOrRelativeDosPath)) {
            return absoluteOrRelativeDosPath;
        }

        string backslashedDosPath = ConvertUtils.ToBackSlashPath(absoluteOrRelativeDosPath);

        // Determine the target drive letter and the remaining path after the optional X: prefix.
        char driveLetter;
        string remainingPath;
        if (StartsWithDosDriveAndVolumeSeparator(backslashedDosPath)) {
            driveLetter = char.ToUpperInvariant(backslashedDosPath[0]);
            remainingPath = backslashedDosPath[2..];
        } else {
            driveLetter = _dosDriveManager.CurrentDrive.DosVolume[0];
            remainingPath = backslashedDosPath;
        }

        bool pathIsRooted = remainingPath.Length > 0 &&
                            (remainingPath[0] == DirectorySeparatorChar ||
                             remainingPath[0] == AltDirectorySeparatorChar);

        // Seed the segment stack with the drive's current directory for non-rooted paths.
        List<string> segments = new();
        if (!pathIsRooted &&
            _dosDriveManager.TryGetValue(driveLetter, out VirtualDrive? drive) &&
            !string.IsNullOrEmpty(drive.CurrentDosDirectory)) {
            foreach (string segment in drive.CurrentDosDirectory.Split(
                DirectorySeparatorChar,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
                segments.Add(segment.ToUpperInvariant());
            }
        }

        // Apply each path element, popping on '..' and skipping '.'.
        foreach (string element in remainingPath.Split(
            DirectorySeparatorChar,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
            if (element == ".") {
                continue;
            }
            if (element == "..") {
                if (segments.Count > 0) {
                    segments.RemoveAt(segments.Count - 1);
                }
                continue;
            }
            if (element.Contains(VolumeSeparatorChar)) {
                continue;
            }
            segments.Add(element.ToUpperInvariant());
        }

        StringBuilder normalizedDosPath = new();
        normalizedDosPath.Append(driveLetter);
        normalizedDosPath.Append(VolumeSeparatorChar);
        normalizedDosPath.Append(DirectorySeparatorChar);
        for (int i = 0; i < segments.Count; i++) {
            if (i > 0) {
                normalizedDosPath.Append(DirectorySeparatorChar);
            }
            normalizedDosPath.Append(segments[i]);
        }

        return normalizedDosPath.ToString();
    }

    /// <summary>
    /// Converts the DOS path to a full host path of the parent directory.<br/>
    /// </summary>
    /// <param name="dosPath">The DOS path to convert.</param>
    /// <returns>A string containing the full path to the parent directory in the host file system, or <c>null</c> if nothing was found.</returns>
    public string? GetFullHostParentPathFromDosOrDefault(string dosPath) {
        string? parentPath = Path.GetDirectoryName(dosPath);
        if (string.IsNullOrWhiteSpace(parentPath)) {
            parentPath = GetFullCurrentDosPathOnDrive(_dosDriveManager.CurrentDrive);
        }
        string? fullHostPath = GetFullHostPathFromDosOrDefault(parentPath);
        if (string.IsNullOrWhiteSpace(fullHostPath)) {
            return null;
        }
        return ConvertUtils.ToSlashFolderPath(fullHostPath);
    }

    private (string hostPrefixPath, string dosRelativePath) DeconstructDosPath(string dosPath) {
        if (IsPathRooted(dosPath)) {
            int length = 1;
            if (StartsWithDosDriveAndVolumeSeparator(dosPath)) {
                length = 3;
            }
            return (_dosDriveManager.CurrentDrive.MountedHostDirectory, dosPath[length..]);
        }

        return StartsWithDosDriveAndVolumeSeparator(dosPath)
            ? (_dosDriveManager[dosPath[0]].MountedHostDirectory, dosPath[2..])
            : (_dosDriveManager.CurrentDrive.MountedHostDirectory, dosPath);
    }

    /// <summary>
    /// Converts the DOS path to a full host path.<br/>
    /// </summary>
    /// <param name="dosPath">The DOS path to convert.</param>
    /// <returns>A string containing the full file path in the host file system, or <c>null</c> if nothing was found.</returns>
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

        EnumerationOptions options = new EnumerationOptions {
            RecurseSubdirectories = false,
            MatchCasing = MatchCasing.CaseInsensitive,
            ReturnSpecialDirectories = false
        };

        string? firstMatch = FindFilesUsingWildCmp(resolvedHostDir, lastSegment, options).FirstOrDefault();
        return string.IsNullOrWhiteSpace(firstMatch) ? null : ConvertUtils.ToSlashPath(firstMatch);
    }

    /// <summary>
    /// Resolves a DOS path to a host path for a file that may not yet exist.
    /// The parent directory must exist; the filename is appended as-is.
    /// </summary>
    /// <param name="dosPath">The DOS path of the new file.</param>
    /// <returns>A host file path, or <c>null</c> if the parent directory cannot be resolved.</returns>
    public string? ResolveNewFilePath(string dosPath) {
        if (string.IsNullOrWhiteSpace(dosPath)) {
            return null;
        }

        dosPath = GetFullDosPathIncludingRoot(dosPath);
        (string hostPrefix, string dosRelativePath) = DeconstructDosPath(dosPath);

        if (string.IsNullOrWhiteSpace(dosRelativePath)) {
            return null;
        }

        string slashedRelative = ConvertUtils.ToSlashPath(dosRelativePath);
        int lastSlash = slashedRelative.LastIndexOf('/');
        string dirPart = lastSlash >= 0 ? slashedRelative[..lastSlash] : string.Empty;
        string fileName = lastSlash >= 0 ? slashedRelative[(lastSlash + 1)..] : slashedRelative;

        if (string.IsNullOrWhiteSpace(fileName)) {
            return null;
        }

        string? resolvedHostDir = ResolveCaseInsensitiveDirectory(hostPrefix, dirPart);
        if (string.IsNullOrWhiteSpace(resolvedHostDir)) {
            return null;
        }

        return ConvertUtils.ToSlashPath(Path.Join(resolvedHostDir, fileName));
    }

    /// <summary>
    /// Converts the DOS path to a full host path, probing for executable extensions (.BAT, .COM, .EXE)
    /// when the path has no extension. Use this only for execution-related path resolution.
    /// </summary>
    /// <param name="dosPath">The DOS path to convert.</param>
    /// <returns>A string containing the full file path in the host file system, or <c>null</c> if nothing was found.</returns>
    public string? GetFullHostExecutablePathFromDosOrDefault(string dosPath) {
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

        EnumerationOptions options = new EnumerationOptions {
            RecurseSubdirectories = false,
            MatchCasing = MatchCasing.CaseInsensitive,
            ReturnSpecialDirectories = false
        };

        string? firstMatch = FindFilesUsingWildCmp(resolvedHostDir, lastSegment, options).FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(firstMatch)) {
            return ConvertUtils.ToSlashPath(firstMatch);
        }

        string? extensionProbeMatch = ResolveExecutableWithoutExtension(resolvedHostDir, lastSegment, options);
        return string.IsNullOrWhiteSpace(extensionProbeMatch) ? null : ConvertUtils.ToSlashPath(extensionProbeMatch);
    }

    private string? ResolveExecutableWithoutExtension(string resolvedHostDir, string lastSegment, EnumerationOptions options) {
        if (string.IsNullOrWhiteSpace(lastSegment)) {
            return null;
        }

        if (lastSegment.Contains('*') || lastSegment.Contains('?') || Path.HasExtension(lastSegment)) {
            return null;
        }

        return ExecutableExtensionLookupOrder
            .Select(extension => FindFilesUsingWildCmp(resolvedHostDir, $"{lastSegment}{extension}", options).FirstOrDefault())
            .FirstOrDefault(match => !string.IsNullOrWhiteSpace(match));
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

    internal static string GetShortFileName(string hostFileName, string hostDir) {
        string rawName = Path.GetFileNameWithoutExtension(hostFileName);
        string rawExtension = Path.GetExtension(hostFileName);

        // Step 1: Uppercase and strip spaces (DOSBox: upcase + RemoveSpaces)
        string upperName = rawName.ToUpperInvariant().Replace(" ", "", StringComparison.Ordinal);
        string upperExtension = rawExtension.ToUpperInvariant().Replace(" ", "", StringComparison.Ordinal);

        // Step 2: Strip leading dots from the name portion
        int leadingDots = 0;
        while (leadingDots < upperName.Length && upperName[leadingDots] == '.') {
            leadingDots++;
        }
        if (leadingDots > 0) {
            upperName = upperName[leadingDots..];
        }

        // Step 3: Determine if a short name with tilde is needed (DOSBox logic)
        bool needsShortName = upperName.Length != rawName.Length; // spaces were removed
        needsShortName = needsShortName || upperName.Length > DosMfnlength; // name > 8 chars
        needsShortName = needsShortName || rawExtension.Length > DosExtlength + 1; // extension > 3 chars (including dot)

        // Step 4: Truncate extension to 3 chars
        string shortExtension;
        if (upperExtension.Length > DosExtlength + 1) {
            shortExtension = upperExtension[..(DosExtlength + 1)]; // ".EXT"
        } else {
            shortExtension = upperExtension;
        }

        if (!needsShortName) {
            // No tilde needed — return uppercased name + extension
            return $"{upperName}{shortExtension}";
        }

        // Step 5: Count collisions with same short-name stem in the directory (DOSBox: CreateShortNameID)
        int shortNr = ComputeShortNameId(hostFileName, upperName, hostDir);

        // Step 6: Build NAMEXX~N format
        string shortNrStr = shortNr.ToString();
        int tildeSize = 1 + shortNrStr.Length; // '~' + digits
        int charsToKeep = Math.Min(upperName.Length, DosMfnlength - tildeSize);
        charsToKeep = Math.Max(charsToKeep, 1);

        StringBuilder shortName = new();
        shortName.Append(upperName.AsSpan(0, charsToKeep));
        shortName.Append('~');
        shortName.Append(shortNrStr);
        shortName.Append(shortExtension);

        return shortName.ToString();
    }

    private static int ComputeShortNameId(string hostFileName, string upperName, string hostDir) {
        if (string.IsNullOrWhiteSpace(hostDir) || !Directory.Exists(hostDir)) {
            return 1;
        }

        // Build the short-name prefix that this file would get (before the ~N part).
        int maxStemChars = Math.Min(upperName.Length, DosMfnlength - 2); // leave room for at least ~1
        maxStemChars = Math.Max(maxStemChars, 1);
        string stemPrefix = upperName[..maxStemChars];

        // Collect ALL entries whose truncated 8.3 stem prefix matches ours — including
        // entries that are already valid 8.3 names (e.g. an existing VERYLO~1.TXT
        // would otherwise be skipped and cause a duplicate short name to be assigned).
        List<string> colliders = new();
        foreach (string entry in Directory.EnumerateFileSystemEntries(hostDir)) {
            string entryFileName = Path.GetFileName(entry);
            string entryBase = Path.GetFileNameWithoutExtension(entryFileName)
                .ToUpperInvariant()
                .Replace(" ", "", StringComparison.Ordinal);

            int entryMaxStem = Math.Min(entryBase.Length, DosMfnlength - 2);
            entryMaxStem = Math.Max(entryMaxStem, 1);
            string entryPrefix = entryBase[..entryMaxStem];

            if (string.Equals(stemPrefix, entryPrefix, StringComparison.OrdinalIgnoreCase)) {
                colliders.Add(entryFileName);
            }
        }

        colliders.Sort(StringComparer.OrdinalIgnoreCase);
        int index = colliders.FindIndex(f => string.Equals(f, hostFileName, StringComparison.OrdinalIgnoreCase));
        return index >= 0 ? index + 1 : colliders.Count + 1;
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
        dosPath = GetFullDosPathIncludingRoot(dosPath);
        (string HostPrefix, string DosRelativePath) = DeconstructDosPath(dosPath);
        return ConvertUtils.ToSlashPath(Path.Join(HostPrefix, DosRelativePath));
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

    /// <summary>
    /// Returns whether the folder or file name already exists, in DOS's case insensitive point of view.
    /// </summary>
    /// <param name="newFileOrDirectoryPath">The name of new file or folder we try to create.</param>
    /// <param name="hostFolder">The full path to the host folder to look into.</param>
    /// <returns>A boolean value indicating if there is any folder or file with the same name.</returns>
    public bool AnyDosDirectoryOrFileWithTheSameName(string newFileOrDirectoryPath, DirectoryInfo hostFolder) =>
        GetTopLevelDirsAndFiles(hostFolder.FullName).Any(x =>
            string.Equals(Path.GetFileName(x), Path.GetFileName(newFileOrDirectoryPath), StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<string> GetTopLevelDirsAndFiles(string hostPath, string searchPattern = "*") {
        return Directory
            .GetDirectories(hostPath, searchPattern)
            .Concat(Directory.GetFiles(hostPath, searchPattern));
    }


    /// <summary>
    /// Resolves the DOS file entry metadata for a host file system path.
    /// </summary>
    /// <param name="hostPath">The full host file system path.</param>
    /// <param name="searchFolder">The host folder used for short name generation.</param>
    /// <returns>A <see cref="DosFileEntryInfo"/> containing the resolved metadata.</returns>
    internal DosFileEntryInfo GetDosFileEntryInfo(string hostPath, string searchFolder) {
        FileSystemInfo entryInfo = Directory.Exists(hostPath)
            ? new DirectoryInfo(hostPath)
            : new FileInfo(hostPath);
        DosFileAttributes dosAttributes = (DosFileAttributes)entryInfo.Attributes;
        uint fileSize = entryInfo is FileInfo fi ? (uint)fi.Length : 0;
        string shortName = GetShortFileName(Path.GetFileName(hostPath), searchFolder);
        return new DosFileEntryInfo(dosAttributes, fileSize, entryInfo.CreationTimeUtc, shortName);
    }

    public IEnumerable<string> FindFilesUsingWildCmp(string searchFolder, string searchPattern,
        EnumerationOptions enumerationOptions) {
        return Directory.EnumerateFileSystemEntries(searchFolder, "*", enumerationOptions)
            .Where(path => WildFileCmp(Path.GetFileName(path), searchPattern));
    }

    public static bool WildFileCmp(string? filename, string? pattern) {
        if (filename is null || pattern is null) {
            return false;
        }

        return WildFileCmp(filename.AsSpan(), pattern.AsSpan());
    }

    private static bool WildFileCmp(ReadOnlySpan<char> sourceFilename, ReadOnlySpan<char> pattern) {
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

        // Skip “hidden” dot-files if the pattern uses wildcards (except "." / "..")
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

        // ---- NAME compare ----
        // DOS semantics: ".EXT" is equivalent to "*.EXT" (empty name matches any)
        bool patternNameIsEmpty = PatternNameIsEmpty(pattern);
        if (!patternNameIsEmpty) {
            if (CompareSegment(fileName, wildName, DosMfnlength) == false) {
                return false;
            }
        }

        // ---- EXT compare (early '*' accept) ----
        return CompareSegment(fileExt, wildExt, DosExtlength) switch {
            true => true,
            false => false,
            // If wild ext has a 4th char, and it's not '*', reject (DOSBox-like behavior)
            _ => wildExtLength <= DosExtlength || wildExt[DosExtlength] == '*'
        };
    }

    /// <summary>
    /// Common 8.3 segment compare.
    /// </summary>
    /// <param name="filenameOrExt">The segment to compare against.</param>
    /// <param name="pattern">The pattern to match.</param>
    /// <param name="length">Maximum number of characters to compare.</param>
    /// <returns>
    /// true - definitively accept (only possible with earlyStarAcceptsTrue and '*' seen)
    /// false - mismatch 
    /// null - matched the segment fully (or name-stopped at '*'), no final decision
    /// </returns>
    /// <remarks>
    /// '?' matches any char (including space padding).
    /// If '*' is encountered:
    /// - For extension compare (earlyStarAcceptsTrue=true): returns true immediately
    /// - For name compare (earlyStarAcceptsTrue=false): treats as "stop comparing here" and returns null
    /// </remarks>
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

        return fileName.Length >= 5 && fileName[0] == '.' &&
               !fileName.Equals(".", StringComparison.Ordinal) &&
               !fileName.Equals("..", StringComparison.Ordinal);
    }

    private static void ToUpperCopy(ReadOnlySpan<char> src, Span<char> dst) {
        src.ToUpperInvariant(dst);
    }

    private static bool PatternNameIsEmpty(ReadOnlySpan<char> pattern) {
        int dotPos = pattern.LastIndexOf('.');
        return dotPos == 0; // begins with a dot, so name part length is zero
    }
}