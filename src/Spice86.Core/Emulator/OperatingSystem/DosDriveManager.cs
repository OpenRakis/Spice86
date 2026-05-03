namespace Spice86.Core.Emulator.OperatingSystem;

using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

/// <summary>
/// The class responsible for centralizing all the mounted DOS drives.
/// </summary>
public class DosDriveManager : IDictionary<char, VirtualDrive> {
    private readonly SortedDictionary<char, VirtualDrive> _driveMap = new();
    private readonly Dictionary<char, MemoryDrive> _memoryDriveMap = new();
    private readonly DosMediaIdTable _mediaIdTable;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="loggerService">The service used to log messages.</param>
    /// <param name="cDriveFolderPath">The host path to be mounted as C:.</param>
    /// <param name="executablePath">The host path to the DOS executable to be launched.</param>
    /// <param name="mediaIdTable">The DOS private-segment media ID table owned by this manager.</param>
    public DosDriveManager(ILoggerService loggerService, string? cDriveFolderPath, string? executablePath, DosMediaIdTable mediaIdTable) {
        _mediaIdTable = mediaIdTable;
        if (string.IsNullOrWhiteSpace(cDriveFolderPath)) {
            cDriveFolderPath = DosPathResolver.GetExeParentFolder(executablePath);
        }
        cDriveFolderPath = ConvertUtils.ToSlashFolderPath(cDriveFolderPath);
        _driveMap.Add('A', new() { DriveLetter = 'A', CurrentDosDirectory = "", MountedHostDirectory = "" });
        _driveMap.Add('B', new() { DriveLetter = 'B', CurrentDosDirectory = "", MountedHostDirectory = "" });
        var cDrive = new VirtualDrive { DriveLetter = 'C', MountedHostDirectory = cDriveFolderPath, CurrentDosDirectory = "" };
        _driveMap.Add('C', cDrive);
        CurrentDrive = cDrive;
        InitializeMediaDescriptors();
        if (loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            loggerService.Verbose("DOS Drives initialized: {@Drives}", _driveMap.Values);
        }
    }

    /// <summary>
    /// The currently selected drive.
    /// </summary>
    public VirtualDrive CurrentDrive { get; set; }

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

    /// <suummary>
    /// Gets the number of DOS drive letters assigned.
    /// </summary>
    public byte NumberOfPotentiallyValidDriveLetters {
        get {
            // At least A: and B:
            return (byte)_driveMap.Count;
        }
    }

    public ICollection<char> Keys => ((IDictionary<char, VirtualDrive>)_driveMap).Keys;

    public ICollection<VirtualDrive> Values => ((IDictionary<char, VirtualDrive>)_driveMap).Values;

    public int Count => ((ICollection<KeyValuePair<char, VirtualDrive>>)_driveMap).Count;

    public bool IsReadOnly => ((ICollection<KeyValuePair<char, VirtualDrive>>)_driveMap).IsReadOnly;

    public VirtualDrive this[char key] { get => ((IDictionary<char, VirtualDrive>)_driveMap)[key]; set => ((IDictionary<char, VirtualDrive>)_driveMap)[key] = value; }


    public const int MaxDriveCount = 26;

    private const byte FloppyMediaDescriptor = 0xF0;
    private const byte FixedDiskMediaDescriptor = 0xF8;

    /// <summary>Writes the FAT media descriptor byte for every drive into the media ID table.</summary>
    private void InitializeMediaDescriptors() {
        for (byte driveIndex = 0; driveIndex < MaxDriveCount; driveIndex++) {
            _mediaIdTable[driveIndex] = MediaDescriptor(driveIndex);
        }
    }

    /// <summary>The segment of the media ID table, used as DS in AH=1Bh/1Ch returns.</summary>
    public ushort MediaIdTableSegment => _mediaIdTable.Segment;

    /// <summary>In-segment offset of the given drive's entry, used as BX in AH=1Bh/1Ch returns.</summary>
    public ushort MediaIdEntryOffset(byte driveIndex) => _mediaIdTable.EntryOffset(driveIndex);

    private byte MediaDescriptor(byte driveIndex) {
        if (driveIndex <= 1) {
            return FloppyMediaDescriptor;
        }
        return FixedDiskMediaDescriptor;
    }

    public void Add(char key, VirtualDrive value) {
        ((IDictionary<char, VirtualDrive>)_driveMap).Add(key, value);
    }

    public bool ContainsKey(char key) {
        return ((IDictionary<char, VirtualDrive>)_driveMap).ContainsKey(key);
    }

    public bool Remove(char key) {
        return ((IDictionary<char, VirtualDrive>)_driveMap).Remove(key);
    }

    public bool TryGetValue(char key, [MaybeNullWhen(false)] out VirtualDrive value) {
        return ((IDictionary<char, VirtualDrive>)_driveMap).TryGetValue(key, out value);
    }

    public void Add(KeyValuePair<char, VirtualDrive> item) {
        ((ICollection<KeyValuePair<char, VirtualDrive>>)_driveMap).Add(item);
    }

    public void Clear() {
        ((ICollection<KeyValuePair<char, VirtualDrive>>)_driveMap).Clear();
    }

    public bool Contains(KeyValuePair<char, VirtualDrive> item) {
        return ((ICollection<KeyValuePair<char, VirtualDrive>>)_driveMap).Contains(item);
    }

    public void CopyTo(KeyValuePair<char, VirtualDrive>[] array, int arrayIndex) {
        ((ICollection<KeyValuePair<char, VirtualDrive>>)_driveMap).CopyTo(array, arrayIndex);
    }

    public bool Remove(KeyValuePair<char, VirtualDrive> item) {
        return ((ICollection<KeyValuePair<char, VirtualDrive>>)_driveMap).Remove(item);
    }

    public IEnumerator<KeyValuePair<char, VirtualDrive>> GetEnumerator() {
        return ((IEnumerable<KeyValuePair<char, VirtualDrive>>)_driveMap).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() {
        return ((IEnumerable)_driveMap).GetEnumerator();
    }

    /// <summary>
    /// Mounts a memory-backed drive (typically Z: for AUTOEXEC.BAT).
    /// </summary>
    /// <param name="drive">The memory drive to mount.</param>
    public void MountMemoryDrive(MemoryDrive drive) {
        _memoryDriveMap[drive.DriveLetter] = drive;
    }

    /// <summary>
    /// Tries to get a mounted memory drive by letter.
    /// </summary>
    /// <param name="driveLetter">The drive letter (e.g., 'Z').</param>
    /// <param name="drive">The memory drive if found; null otherwise.</param>
    /// <returns>True if memory drive exists; false otherwise.</returns>
    public bool TryGetMemoryDrive(char driveLetter, [MaybeNullWhen(false)] out MemoryDrive drive) {
        return _memoryDriveMap.TryGetValue(driveLetter, out drive);
    }
}
