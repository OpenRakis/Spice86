namespace Spice86.Core.Emulator.OperatingSystem;

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
    private readonly SortedDictionary<char, HostFolderDrive?> _driveMap = new();
    private readonly ILoggerService _loggerService;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="loggerService">The service used to log messages.</param>
    /// <param name="cDriveFolderPath">The host path to be mounted as C:.</param>
    /// <param name="executablePath">The host path to the DOS executable to be launched.</param>
    public DosDriveManager(ILoggerService loggerService, string? cDriveFolderPath, string? executablePath) {
        if (string.IsNullOrWhiteSpace(cDriveFolderPath)) {
            cDriveFolderPath = DosPathResolver.GetExeParentFolder(executablePath);
        }
        _loggerService = loggerService;
        cDriveFolderPath = ConvertUtils.ToSlashFolderPath(cDriveFolderPath);
        _driveMap.Add('A', null);
        _driveMap.Add('B', null);
        _driveMap.Add('C', new HostFolderDrive { DriveLetter = 'C', MountedHostDirectory = cDriveFolderPath, CurrentDosDirectory = "" });
        CurrentDrive = _driveMap.ElementAt(2).Value!;
        if(loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            loggerService.Verbose("DOS Drives initialized: {@Drives}", _driveMap.Values);
        }
    }

    /// <summary>
    /// The currently selected drive.
    /// </summary>
    public HostFolderDrive CurrentDrive { get; set; }

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

    public ICollection<char> Keys => ((IDictionary<char, HostFolderDrive>)_driveMap).Keys;

    public ICollection<HostFolderDrive> Values => ((IDictionary<char, HostFolderDrive>)_driveMap).Values;

    public int Count => ((ICollection<KeyValuePair<char, HostFolderDrive>>)_driveMap).Count;

    public bool IsReadOnly => ((ICollection<KeyValuePair<char, HostFolderDrive>>)_driveMap).IsReadOnly;

    public HostFolderDrive this[char key] { get => ((IDictionary<char, HostFolderDrive>)_driveMap)[key]; set => ((IDictionary<char, HostFolderDrive>)_driveMap)[key] = value; }


    public const int MaxDriveCount = 26;

    public void Add(char key, HostFolderDrive value) {
        ((IDictionary<char, HostFolderDrive>)_driveMap).Add(key, value);
    }

    public bool ContainsKey(char key) {
        return ((IDictionary<char, HostFolderDrive>)_driveMap).ContainsKey(key);
    }

    public bool Remove(char key) {
        return ((IDictionary<char, HostFolderDrive>)_driveMap).Remove(key);
    }

    public bool TryGetValue(char key, [MaybeNullWhen(false)] out HostFolderDrive value) {
        return ((IDictionary<char, HostFolderDrive>)_driveMap).TryGetValue(key, out value);
    }

    public void Add(KeyValuePair<char, HostFolderDrive> item) {
        ((ICollection<KeyValuePair<char, HostFolderDrive>>)_driveMap).Add(item);
    }

    public void Clear() {
        ((ICollection<KeyValuePair<char, HostFolderDrive>>)_driveMap).Clear();
    }

    public bool Contains(KeyValuePair<char, HostFolderDrive> item) {
        return ((ICollection<KeyValuePair<char, HostFolderDrive>>)_driveMap).Contains(item);
    }

    public void CopyTo(KeyValuePair<char, HostFolderDrive>[] array, int arrayIndex) {
        ((ICollection<KeyValuePair<char, HostFolderDrive>>)_driveMap).CopyTo(array, arrayIndex);
    }

    public bool Remove(KeyValuePair<char, HostFolderDrive> item) {
        return ((ICollection<KeyValuePair<char, HostFolderDrive>>)_driveMap).Remove(item);
    }

    public IEnumerator<KeyValuePair<char, HostFolderDrive>> GetEnumerator() {
        return ((IEnumerable<KeyValuePair<char, HostFolderDrive>>)_driveMap).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() {
        return ((IEnumerable)_driveMap).GetEnumerator();
    }
}
