namespace Spice86.Core.Emulator.OperatingSystem;

using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Utils;

using System.Diagnostics;
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
            if (_dosDriveManager.TryGetDriveAtIndex(driveNumber - 1, out VirtualDrive? virtualDrive)) {
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

    private static bool IsWithinMountPoint(string hostFullPath, VirtualDrive? virtualDrive) =>
        virtualDrive is not null && hostFullPath.StartsWith(virtualDrive.MountedHostDirectory);

    /// <summary>
    /// Sets the current DOS folder.
    /// </summary>
    /// <param name="dosPath">The new DOS path to use as the current DOS folder.</param>
    /// <returns>A <see cref="DosFileOperationResult"/> that details the result of the operation.</returns>
    public DosFileOperationResult SetCurrentDir(string dosPath) {
        string? fullDosPath = GetFullDosPathIncludingRoot(dosPath);

        if (fullDosPath is null || !StartsWithDosDriveAndVolumeSeparator(fullDosPath)) {
            return DosFileOperationResult.Error(DosErrorCode.PathNotFound);
        }

        string? hostPath = GetFullHostPathFromDosOrDefault(fullDosPath);
        if (!string.IsNullOrWhiteSpace(hostPath)) {
            return SetCurrentDirValue(fullDosPath[0], hostPath, fullDosPath);
        } else {
            return DosFileOperationResult.Error(DosErrorCode.PathNotFound);
        }
    }

    /// <summary>Gets the associated drive letter from the specified DOS path.</summary>
    /// <param name="path">The DOS path to resolve.</param>
    /// <param name="driveIndex">The zero-based DOS drive index or -1 on failure.</param>
    /// <param name="isDrivePath">
    /// <see langword="true"/> if the specified path starts with a drive letter and volume separator; otherwise,
    /// <see langword="false"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if the path has a valid drive letter or references the current drive (with a valid drive
    /// index); otherwise, <see langword="false"/>.
    /// </returns>
    public bool TryGetDosDriveIndexFromDosPath(ReadOnlySpan<char> path, out int driveIndex, out bool isDrivePath) {
        if (path.Length >= 2 && path[1] == VolumeSeparatorChar) {
            // DOS path with drive specification.
            isDrivePath = true;

            driveIndex = DosDriveManager.GetDriveIndex(path[0]);
            if (driveIndex == -1) {
                // Invalid DOS path (bad drive letter).
                return false;
            }
        } else {
            // DOS path without drive specification (current drive).
            isDrivePath = false;

            // Perform a defensive check and avoid throwing an exception if the current drive letter/index is invalid.
            driveIndex = DosDriveManager.GetDriveIndex(_dosDriveManager.CurrentDrive.DriveLetter);
            if (driveIndex == -1) {
                // Current drive has a bad drive letter.
                return false;
            }
        }

        Debug.Assert(driveIndex is >= 0 and < DosDriveManager.MaxDriveCount);
        return true;
    }

    private DosFileOperationResult SetCurrentDirValue(char driveLetter, string? hostFullPath, string fullDosPath) {
        if (string.IsNullOrWhiteSpace(hostFullPath) ||
            !IsWithinMountPoint(hostFullPath, _dosDriveManager.TryGetDrive(driveLetter, out VirtualDrive? vDrive) ? vDrive : null) ||
            Encoding.ASCII.GetByteCount(fullDosPath) > MaxPathLength) {
            return DosFileOperationResult.Error(DosErrorCode.PathNotFound);
        }

        _dosDriveManager[driveLetter].CurrentDosDirectory = fullDosPath[3..];
        return DosFileOperationResult.NoValue();
    }

    internal DosPathBuilderResult GetFullDosPathIncludingRoot(ReadOnlySpan<char> dosPath,
            ref DosPathBuilder pathBuilder) {
        Debug.Assert(!pathBuilder.IsFrozen);
        Debug.Assert(pathBuilder.Length == 0);
        pathBuilder.DebugValidateState();

        ReadOnlySpan<char> dosPathSpan = dosPath.TrimStart();
        if (!TryGetDosDriveIndexFromDosPath(dosPathSpan, out int driveIndex, out bool isDrivePath)) {
            return DosPathBuilderResult.InvalidDriveSpecification;
        }

        // Try to set drive specification on path builder (this should always succeed as long as the path builder is
        // in a valid state).
        DosPathBuilderResult appendResult = pathBuilder.SetDriveIndex(driveIndex);
        if (appendResult != DosPathBuilderResult.Success) {
            return appendResult;
        }

        // Remove drive specification from input path (if specified).
        if (isDrivePath) {
            dosPathSpan = dosPathSpan[2..];
        }

        // Handle relative paths for mounted drives.
        // It does not matter whether the input has a drive specification or not; the path is a relative path if the
        // first character (after the optional drive specification) is not a directory separator. If the path is empty,
        // then it is treated as a relative path to the current directory on the chosen drive.
        // See: https://learn.microsoft.com/en-us/windows/win32/fileio/naming-a-file#fully-qualified-vs-relative-paths
        bool isRelativePath = dosPathSpan.IsEmpty || dosPathSpan[0] is not (DirectorySeparatorChar or AltDirectorySeparatorChar);

        // Try to append current DOS directory on specified drive if resolving a relative path.
        if (isRelativePath && _dosDriveManager.TryGetDriveAtIndex(driveIndex, out DosDriveBase? drive)) {
            appendResult = pathBuilder.AppendRelativePath(drive.CurrentDosDirectory, out _);
            if (appendResult != DosPathBuilderResult.Success) {
                return appendResult;
            }
        }

        // Handle remaining path elements.
        appendResult = pathBuilder.AppendRelativePath(dosPathSpan, out bool endsWithSlash);
        if (appendResult != DosPathBuilderResult.Success) {
            return appendResult;
        }

        // Make sure full path ends with a directory separator or file name. Also freeze the path builder to prevent
        // further modifications to the path.
        if (endsWithSlash) {
            // This will implicitly freeze the path builder (no need to call Freeze() after this).
            pathBuilder.AppendFinalDirectorySeparator();
        } else {
            pathBuilder.Freeze();
        }

        Debug.Assert(pathBuilder.IsFrozen);
        pathBuilder.DebugValidateState();
        return DosPathBuilderResult.Success;
    }

    internal DosPathBuilderResult GetFullDosPathIncludingRoot(ReadOnlySpan<char> dosPath, out string? fullDosPath) {
        // NOTE: Make sure the path builder is disposed before returning from this method.
        // TODO: Set path builder special file name settings?
        DosPathBuilder pathBuilder = new(
            stackalloc char[MaxPathLength],
            stackalloc int[DosPathBuilder.DefaultStackLength]);

        DosPathBuilderResult result = GetFullDosPathIncludingRoot(dosPath, ref pathBuilder);
        if (result != DosPathBuilderResult.Success) {
            fullDosPath = null;
            pathBuilder.Dispose();
            return result;
        }

        fullDosPath = pathBuilder.ToStringWithDispose();
        return DosPathBuilderResult.Success;
    }

    internal DosPathBuilderResult GetFullDosPathIncludingRoot(string? dosPath, out string? fullDosPath) {
        // NOTE: Make sure the path builder is disposed before returning from this method.
        // TODO: Set path builder special file name settings?
        DosPathBuilder pathBuilder = new(
            stackalloc char[MaxPathLength],
            stackalloc int[DosPathBuilder.DefaultStackLength]);

        DosPathBuilderResult result = GetFullDosPathIncludingRoot(dosPath, ref pathBuilder);
        if (result != DosPathBuilderResult.Success) {
            fullDosPath = null;
            pathBuilder.Dispose();
            return result;
        }

        // Slight memory optimization if original input string is not null and is an exact match to the path builder.
        // There is a slight time-memory tradeoff here (prefer keeping the memory heap smaller by not allocating).
        ReadOnlySpan<char> pathBuilderSpan = pathBuilder.AsSpan();
        if (dosPath is not null && pathBuilderSpan.SequenceEqual(dosPath)) {
            fullDosPath = dosPath;
            pathBuilder.Dispose();
            return DosPathBuilderResult.Success;
        }

        fullDosPath = pathBuilderSpan.ToString();
        pathBuilder.Dispose();
        return DosPathBuilderResult.Success;
    }

    internal string? GetFullDosPathIncludingRoot(string? dosPath) {
        DosPathBuilderResult result = GetFullDosPathIncludingRoot(dosPath, out string? fullDosPath);
        // It's either successful with a non-null string or failure with a null string.
        Debug.Assert((result != DosPathBuilderResult.Success) ^ (fullDosPath is not null));
        return result == DosPathBuilderResult.Success ? fullDosPath : null;
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

    private (string? hostPrefixPath, string dosRelativePath) DeconstructDosPath(string dosPath) {
        // This method is currently only called with paths that have been processed via GetFullDosPathIncludingRoot.
        // Thus the input path here should always be a full rooted path with a drive specification.
        if (dosPath.Length < 3 || !char.IsAsciiLetter(dosPath[0]) || dosPath[1] != VolumeSeparatorChar ||
                dosPath[2] != DirectorySeparatorChar) {
            throw new ArgumentException("Given DOS path is not a full rooted path with a drive specification.", nameof(dosPath));
        }

        // Avoid throwing an exception if the drive does not exist. Let the caller figure out what to do by setting the
        // host prefix path to null. Technically the drive letter will always be a valid in the drive manager, but it
        // is not always guaranteed to be a VirtualDrive.
        if (!_dosDriveManager.TryGetDrive(dosPath[0], out VirtualDrive? drive)) {
            return (null, dosPath[3..]);
        }

        return (drive.MountedHostDirectory, dosPath[3..]);
    }

    /// <summary>
    /// Converts the DOS path to a full host path.
    /// </summary>
    /// <param name="dosPath">The DOS path to convert.</param>
    /// <returns>A string containing the full file path in the host file system, or <see langword="null"/> if nothing was found or the DOS path cannot be resolved.</returns>
    public string? GetFullHostPathFromDosOrDefault(string? dosPath) {
        (string resolvedHostDir, string lastSegment)? components = ResolveDosPathComponents(dosPath);
        if (components is null) {
            return null;
        }

        return ResolveFileInDirectory(components.Value.resolvedHostDir, components.Value.lastSegment);
    }

    /// <summary>
    /// Resolves a DOS path to a host path for a file that may not yet exist.
    /// The parent directory must exist; the filename is appended as-is.
    /// </summary>
    /// <param name="dosPath">The DOS path of the new file.</param>
    /// <returns>A host file path, or <see langword="null"/> if the parent directory or the DOS path cannot be resolved.</returns>
    public string? ResolveNewFilePath(string? dosPath) {
        if (string.IsNullOrWhiteSpace(dosPath)) {
            return null;
        }

        dosPath = GetFullDosPathIncludingRoot(dosPath);
        if (dosPath is null) {
            return null;
        }

        (string? hostPrefix, string dosRelativePath) = DeconstructDosPath(dosPath);
        if (hostPrefix is null) {
            return null;
        }

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
    /// <returns>A string containing the full file path in the host file system, or <see langword="null"/> if nothing was found or the DOS path cannot be resolved.</returns>
    public string? GetFullHostExecutablePathFromDosOrDefault(string? dosPath) {
        (string resolvedHostDir, string lastSegment)? components = ResolveDosPathComponents(dosPath);
        if (components is null) {
            return null;
        }

        string resolvedHostDir = components.Value.resolvedHostDir;
        string lastSegment = components.Value.lastSegment;

        string? result = ResolveFileInDirectory(resolvedHostDir, lastSegment);
        if (result is not null) {
            return result;
        }

        string? extensionProbeMatch = TryResolveExecutableWithoutExtension(resolvedHostDir, lastSegment);
        return string.IsNullOrWhiteSpace(extensionProbeMatch) ? null : ConvertUtils.ToSlashPath(extensionProbeMatch);
    }

    /// <summary>
    /// Resolves the DOS path into its host directory and filename components.
    /// Returns <see langword="null"/> when the path cannot be resolved (invalid, empty, or missing directory).
    /// When the path refers to a directory (no filename), <c>lastSegment</c> is empty.
    /// </summary>
    private (string resolvedHostDir, string lastSegment)? ResolveDosPathComponents(string? dosPath) {
        if (string.IsNullOrWhiteSpace(dosPath)) {
            return null;
        }

        dosPath = GetFullDosPathIncludingRoot(dosPath);
        if (dosPath is null) {
            return null;
        }

        (string? hostPrefix, string dosRelativePath) = DeconstructDosPath(dosPath);
        if (hostPrefix is null) {
            return null;
        }

        if (string.IsNullOrWhiteSpace(dosRelativePath)) {
            return (ConvertUtils.ToSlashPath(hostPrefix), string.Empty);
        }

        string slashedRelative = ConvertUtils.ToSlashPath(dosRelativePath);
        int lastSlash = slashedRelative.LastIndexOf('/');
        string dirPart = lastSlash >= 0 ? slashedRelative[..lastSlash] : string.Empty;
        string lastSegment = lastSlash >= 0 ? slashedRelative[(lastSlash + 1)..] : slashedRelative;

        string? resolvedHostDir = ResolveCaseInsensitiveDirectory(hostPrefix, dirPart);
        if (string.IsNullOrWhiteSpace(resolvedHostDir)) {
            return null;
        }

        return (resolvedHostDir, lastSegment);
    }

    /// <summary>
    /// Resolves a filename within a host directory, trying an exact case-insensitive match first
    /// to avoid 8.3 truncation false positives, then falling back to DOS wildcard comparison.
    /// </summary>
    private string? ResolveFileInDirectory(string resolvedHostDir, string lastSegment) {
        if (string.IsNullOrWhiteSpace(lastSegment)) {
            return ConvertUtils.ToSlashPath(resolvedHostDir);
        }

        EnumerationOptions options = new EnumerationOptions {
            RecurseSubdirectories = false,
            MatchCasing = MatchCasing.CaseInsensitive,
            ReturnSpecialDirectories = false
        };

        // Try exact case-insensitive match first to avoid 8.3 truncation false positives
        // (e.g. bios_int70_wait.com and bios_int1a.com both truncating to BIOS_INT.COM).
        string? exactMatch = Directory
            .EnumerateFileSystemEntries(resolvedHostDir, lastSegment, options)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(exactMatch)) {
            return ConvertUtils.ToSlashPath(exactMatch);
        }

        string? firstMatch = FindFilesUsingWildCmp(resolvedHostDir, lastSegment, options).FirstOrDefault();
        return string.IsNullOrWhiteSpace(firstMatch) ? null : ConvertUtils.ToSlashPath(firstMatch);
    }

    private string? TryResolveExecutableWithoutExtension(string resolvedHostDir, string lastSegment) {
        if (string.IsNullOrWhiteSpace(lastSegment)) {
            return null;
        }

        if (lastSegment.Contains('*') || lastSegment.Contains('?') || Path.HasExtension(lastSegment)) {
            return null;
        }

        return ExecutableExtensionLookupOrder
            .Select(extension => ResolveFileInDirectory(resolvedHostDir, $"{lastSegment}{extension}"))
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

        // Step 1: Uppercase and strip spaces
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

        // Step 3: Determine if a short name with tilde is needed
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

        // Step 5: Count collisions with same short-name stem in the directory
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
    /// <returns>A string containing the combination of the host path and the DOS path, or <see langword="null"/> if the DOS path cannot be resolved.</returns>
    public string? PrefixWithHostDirectory(string? dosPath) {
        if (string.IsNullOrWhiteSpace(dosPath)) {
            return dosPath;
        }

        dosPath = GetFullDosPathIncludingRoot(dosPath);
        if (dosPath is null) {
            return null;
        }

        (string? hostPrefix, string dosRelativePath) = DeconstructDosPath(dosPath);
        if (hostPrefix is null) {
            return null;
        }

        return ConvertUtils.ToSlashPath(Path.Join(hostPrefix, dosRelativePath));
    }

    private bool StartsWithDosDriveAndVolumeSeparator(string dosPath) =>
        dosPath.Length >= 2 &&
        DosDriveManager.GetDriveIndex(dosPath[0]) != -1 &&
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
        // wild ext needs an extra slot to check the 4th char
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
        extLength = fileExtRaw.Length; // actual (untruncated) length for the 4th-char check
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