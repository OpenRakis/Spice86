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
public class DosDriveManager : IDictionary<char, FolderDrive> {
    private readonly SortedDictionary<char, FolderDrive> _driveMap = new();
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
        _driveMap.Add('A', new NullDrive(_loggerService, "Empty floppy A", 0) { DriveLetter = 'A', MountedHostDirectory = "", CurrentDosDirectory = "" });
        _driveMap.Add('B', new NullDrive(_loggerService, "Empty floppy A", 1) { DriveLetter = 'A', MountedHostDirectory = "", CurrentDosDirectory = "" });
        _driveMap.Add('C', new FolderDrive(_loggerService, "First hard disk drive", 2) { DriveLetter = 'C', MountedHostDirectory = cDriveFolderPath, CurrentDosDirectory = "" });
        CurrentDrive = _driveMap.ElementAt(2).Value;
        if(loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            loggerService.Verbose("DOS Drives initialized: {@Drives}", _driveMap.Values);
        }
    }

    /// <summary>
    /// The currently selected drive.
    /// </summary>
    public FolderDrive CurrentDrive { get; set; }

    /// <summary>
    /// Gets the current DOS drive letter.
    /// </summary>
    public char CurrentDriveLetter => CurrentDrive.DriveLetter;


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
        if(zeroBasedIndex > DriveLetters.Count - 1) {
            return false;
        }
        if (zeroBasedIndex > _driveMap.Count - 1) {
            return false;
        }
        return _driveMap.ElementAtOrDefault(zeroBasedIndex).Value is not NullDrive;
    }

    public byte NumberOfPotentiallyValidDriveLetters {
        get {
            // At least A: and B:
            return (byte)_driveMap.Count;
        }
    }

    public ICollection<char> Keys => ((IDictionary<char, FolderDrive>)_driveMap).Keys;

    public ICollection<FolderDrive> Values => ((IDictionary<char, FolderDrive>)_driveMap).Values;

    public int Count => ((ICollection<KeyValuePair<char, FolderDrive>>)_driveMap).Count;

    public bool IsReadOnly => ((ICollection<KeyValuePair<char, FolderDrive>>)_driveMap).IsReadOnly;

    public FolderDrive this[char key] { get => ((IDictionary<char, FolderDrive>)_driveMap)[key]; set => ((IDictionary<char, FolderDrive>)_driveMap)[key] = value; }


    public const int MaxDriveCount = 26;

    public void Add(char key, FolderDrive value) {
        ((IDictionary<char, FolderDrive>)_driveMap).Add(key, value);
    }

    public bool ContainsKey(char key) {
        return ((IDictionary<char, FolderDrive>)_driveMap).ContainsKey(key);
    }

    public bool Remove(char key) {
        return ((IDictionary<char, FolderDrive>)_driveMap).Remove(key);
    }

    public bool TryGetValue(char key, [MaybeNullWhen(false)] out FolderDrive value) {
        return ((IDictionary<char, FolderDrive>)_driveMap).TryGetValue(key, out value);
    }

    public void Add(KeyValuePair<char, FolderDrive> item) {
        ((ICollection<KeyValuePair<char, FolderDrive>>)_driveMap).Add(item);
    }

    public void Clear() {
        ((ICollection<KeyValuePair<char, FolderDrive>>)_driveMap).Clear();
    }

    public bool Contains(KeyValuePair<char, FolderDrive> item) {
        return ((ICollection<KeyValuePair<char, FolderDrive>>)_driveMap).Contains(item);
    }

    public void CopyTo(KeyValuePair<char, FolderDrive>[] array, int arrayIndex) {
        ((ICollection<KeyValuePair<char, FolderDrive>>)_driveMap).CopyTo(array, arrayIndex);
    }

    public bool Remove(KeyValuePair<char, FolderDrive> item) {
        return ((ICollection<KeyValuePair<char, FolderDrive>>)_driveMap).Remove(item);
    }

    public IEnumerator<KeyValuePair<char, FolderDrive>> GetEnumerator() {
        return ((IEnumerable<KeyValuePair<char, FolderDrive>>)_driveMap).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() {
        return ((IEnumerable)_driveMap).GetEnumerator();
    }
}
