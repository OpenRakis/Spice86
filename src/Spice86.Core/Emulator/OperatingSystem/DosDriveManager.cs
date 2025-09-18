namespace Spice86.Core.Emulator.OperatingSystem;

using Spice86.Core.Emulator.OperatingSystem.Interfaces;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

/// <summary>
/// The class responsible for centralizing all the mounted DOS drives.
/// </summary>
public class DosDriveManager : IDictionary<char, HostFolderDrive> {
    private readonly SortedDictionary<char, (HostFolderDrive drive, IDosPathResolver resolver)?> _driveMap = new();
    private readonly ILoggerService _loggerService;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="loggerService">The service used to log messages.</param>
    /// <param name="cDriveFolderPath">The host path to be mounted as C:.</param>
    /// <param name="executablePath">The host path to the DOS executable to be launched.</param>
    public DosDriveManager(ILoggerService loggerService, string? cDriveFolderPath, string? executablePath) {
        if (string.IsNullOrWhiteSpace(cDriveFolderPath)) {
            cDriveFolderPath = HostFolderFileSystemResolver.GetExeParentFolder(executablePath);
        }
        _loggerService = loggerService;

        cDriveFolderPath = ConvertUtils.ToSlashFolderPath(cDriveFolderPath);
        _driveMap.Add('A', null);
        _driveMap.Add('B', null);

        // Create C: drive with resolver
        var cDrive = new HostFolderDrive {
            DriveLetter = 'C',
            MountedHostDirectory = cDriveFolderPath,
            CurrentDosDirectory = ""
        };
        var cResolver = new HostFolderFileSystemResolver(cDrive, loggerService, () => this);
        _driveMap.Add('C', (cDrive, cResolver));

        CurrentDriveEntry = _driveMap.ElementAt(2).Value!.Value;

        if (loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            loggerService.Verbose("DOS Drives initialized: {@Drives}", _driveMap.Values.Where(x => x.HasValue).Select(x => x!.Value.drive));
        }
    }

    /// <summary>
    /// Changes the current drive entry to the specified drive letter.
    /// </summary>
    /// <param name="driveLetter">The drive letter to change to</param>
    /// <returns>True if the drive exists and was successfully set as current, false otherwise</returns>
    public bool ChangeCurrentDriveEntry(char driveLetter) {
        if (TryGetDriveEntry(driveLetter, out var entry)) {
            CurrentDriveEntry = entry;
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
                _loggerService.Verbose("Changed current drive to {DriveLetter}: ({MountedDirectory})",
                    driveLetter, entry.drive.MountedHostDirectory);
            }
            return true;
        }
        return false;
    }

    /// <summary>
    /// Changes the current drive entry to the specified drive index.
    /// </summary>
    /// <param name="driveIndex">The zero-based drive index (0=A:, 1=B:, 2=C:, etc.)</param>
    /// <returns>True if the drive exists and was successfully set as current, false otherwise</returns>
    public bool ChangeCurrentDriveEntry(byte driveIndex) {
        char driveLetter = DriveLetters.Keys.ElementAtOrDefault(driveIndex);
        if (driveLetter == default) {
            return false;
        }
        return ChangeCurrentDriveEntry(driveLetter);
    }

    /// <summary>
    /// The currently selected drive entry (drive and resolver).
    /// </summary>
    public (HostFolderDrive drive, IDosPathResolver resolver) CurrentDriveEntry { get; private set; }

    /// <summary>
    /// The currently selected drive.
    /// </summary>
    public HostFolderDrive CurrentDrive => CurrentDriveEntry.drive;

    /// <summary>
    /// Gets the DOS path resolver for the specified drive letter.
    /// </summary>
    /// <param name="driveLetter">The drive letter (A, B, C, etc.)</param>
    /// <returns>The DOS path resolver for the drive</returns>
    /// <exception cref="InvalidOperationException">If the drive doesn't exist or is not mounted</exception>
    public IDosPathResolver GetDosPathResolver(char driveLetter) {
        if (TryGetDriveEntry(driveLetter, out (HostFolderDrive drive, IDosPathResolver resolver) entry)) {
            return entry.resolver;
        }
        throw new InvalidOperationException($"Drive {driveLetter}: is not mounted or does not exist");
    }

    /// <summary>
    /// Gets the DOS path resolver for the specified drive index.
    /// </summary>
    /// <param name="driveIndex">The zero-based drive index (0=A:, 1=B:, 2=C:, etc.)</param>
    /// <returns>The DOS path resolver for the drive</returns>
    /// <exception cref="InvalidOperationException">If the drive doesn't exist or is not mounted</exception>
    public IDosPathResolver GetDosPathResolver(byte driveIndex) {
        char driveLetter = DriveLetters.Keys.ElementAtOrDefault(driveIndex);
        if (driveLetter == default) {
            throw new InvalidOperationException($"Invalid drive index: {driveIndex}");
        }
        return GetDosPathResolver(driveLetter);
    }

    /// <summary>
    /// Gets the DOS path resolver for the current drive.
    /// </summary>
    /// <returns>The DOS path resolver for the current drive</returns>
    public IDosPathResolver GetCurrentDosPathResolver() => CurrentDriveEntry.resolver;

    /// <summary>
    /// Gets the drive entry (drive and resolver) for the specified drive letter.
    /// </summary>
    /// <param name="driveLetter">The drive letter</param>
    /// <returns>The drive entry</returns>
    /// <exception cref="InvalidOperationException">If the drive doesn't exist or is not mounted</exception>
    public (HostFolderDrive drive, IDosPathResolver resolver) GetDriveEntry(char driveLetter) {
        if (TryGetDriveEntry(driveLetter, out (HostFolderDrive drive, IDosPathResolver resolver) entry)) {
            return entry;
        }
        throw new InvalidOperationException($"Drive {driveLetter}: is not mounted or does not exist");
    }

    /// <summary>
    /// Tries to get the drive entry for the specified drive letter.
    /// </summary>
    /// <param name="driveLetter">The drive letter</param>
    /// <param name="entry">The drive entry if found</param>
    /// <returns>True if the drive exists and is mounted, false otherwise</returns>
    public bool TryGetDriveEntry(char driveLetter, [NotNullWhen(true)] out (HostFolderDrive drive, IDosPathResolver resolver) entry) {
        driveLetter = char.ToUpperInvariant(driveLetter);
        if (_driveMap.TryGetValue(driveLetter, out (HostFolderDrive drive, IDosPathResolver resolver)? driveEntry) && driveEntry.HasValue) {
            entry = driveEntry.Value;
            return true;
        }
        entry = default;
        return false;
    }

    internal static readonly ImmutableSortedDictionary<char, byte> DriveLetters = new Dictionary<char, byte>() {
            { 'A', 0 },
            { 'B', 1 },
            { 'C', 2 },
            { 'D', 3 },
            { 'E', 4 },
            { 'F', 5 },
            { 'G', 6 },
            { 'H', 7 },
            { 'I', 8 },
            { 'J', 9 },
            { 'K', 10 },
            { 'L', 11 },
            { 'M', 12 },
            { 'N', 13 },
            { 'O', 14 },
            { 'P', 15 },
            { 'Q', 16 },
            { 'R', 17 },
            { 'S', 18 },
            { 'T', 19 },
            { 'U', 20 },
            { 'V', 21 },
            { 'W', 22 },
            { 'X', 23 },
            { 'Y', 24 },
            { 'Z', 25 }
        }.ToImmutableSortedDictionary();

    /// <summary>
    /// Gets the current DOS drive zero based index.
    /// </summary>
    public byte CurrentDriveIndex => DriveLetters[CurrentDrive.DriveLetter];

    internal bool HasDriveAtIndex(ushort zeroBasedIndex) {
        if (zeroBasedIndex > _driveMap.Count - 1) {
            return false;
        }
        return true;
    }

    public byte NumberOfPotentiallyValidDriveLetters {
        get {
            // At least A: and B:
            return (byte)_driveMap.Count;
        }
    }

    // IDictionary implementation - now works with the drive part only for backward compatibility
    public ICollection<char> Keys => _driveMap.Keys;

    public ICollection<HostFolderDrive> Values => _driveMap.Values
        .Where(x => x.HasValue)
        .Select(x => x!.Value.drive)
        .ToList();

    public int Count => _driveMap.Count(x => x.Value.HasValue);

    public bool IsReadOnly => false;

    public HostFolderDrive this[char key] {
        get => GetDriveEntry(key).drive;
        set {
            char upperKey = char.ToUpperInvariant(key);
            if (_driveMap.TryGetValue(upperKey, out (HostFolderDrive drive, IDosPathResolver resolver)? existing) && existing.HasValue) {
                // Update existing drive, keep resolver
                _driveMap[upperKey] = (value, existing.Value.resolver);
            } else {
                // Create new resolver for new drive
                var resolver = new HostFolderFileSystemResolver(value, _loggerService, () => this);
                _driveMap[upperKey] = (value, resolver);
            }
        }
    }

    public const int MaxDriveCount = 26;

    public void Add(char key, HostFolderDrive value) {
        char upperKey = char.ToUpperInvariant(key);
        if (_driveMap.ContainsKey(upperKey) && _driveMap[upperKey].HasValue) {
            throw new ArgumentException($"Drive {upperKey}: already exists");
        }
        HostFolderFileSystemResolver resolver = new HostFolderFileSystemResolver(value, _loggerService, () => this);
        _driveMap[upperKey] = (value, resolver);
    }

    public bool ContainsKey(char key) {
        char upperKey = char.ToUpperInvariant(key);
        return _driveMap.TryGetValue(upperKey, out (HostFolderDrive drive, IDosPathResolver resolver)? entry) && entry.HasValue;
    }

    public bool Remove(char key) {
        char upperKey = char.ToUpperInvariant(key);
        if (_driveMap.ContainsKey(upperKey)) {
            _driveMap[upperKey] = null;
            return true;
        }
        return false;
    }

    public bool TryGetValue(char key, [MaybeNullWhen(false)] out HostFolderDrive value) {
        if (TryGetDriveEntry(key, out (HostFolderDrive drive, IDosPathResolver resolver) entry)) {
            value = entry.drive;
            return true;
        }
        value = null;
        return false;
    }

    public void Add(KeyValuePair<char, HostFolderDrive> item) {
        Add(item.Key, item.Value);
    }

    public void Clear() {
        foreach (char key in _driveMap.Keys.ToList()) {
            _driveMap[key] = null;
        }
    }

    public bool Contains(KeyValuePair<char, HostFolderDrive> item) {
        return TryGetValue(item.Key, out HostFolderDrive? drive) && ReferenceEquals(drive, item.Value);
    }

    public void CopyTo(KeyValuePair<char, HostFolderDrive>[] array, int arrayIndex) {
        KeyValuePair<char, HostFolderDrive>[] items = _driveMap
            .Where(x => x.Value.HasValue)
            .Select(x => new KeyValuePair<char, HostFolderDrive>(x.Key, x.Value!.Value.drive))
            .ToArray();
        items.CopyTo(array, arrayIndex);
    }

    public bool Remove(KeyValuePair<char, HostFolderDrive> item) {
        if (Contains(item)) {
            return Remove(item.Key);
        }
        return false;
    }

    public IEnumerator<KeyValuePair<char, HostFolderDrive>> GetEnumerator() {
        return _driveMap
            .Where(x => x.Value.HasValue)
            .Select(x => new KeyValuePair<char, HostFolderDrive>(x.Key, x.Value!.Value.drive))
            .GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() {
        return GetEnumerator();
    }
}
