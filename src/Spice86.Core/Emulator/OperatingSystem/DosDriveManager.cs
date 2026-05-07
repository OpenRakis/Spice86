namespace Spice86.Core.Emulator.OperatingSystem;

using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

/// <summary>
/// The class responsible for centralizing all the mounted DOS drives.
/// </summary>
public class DosDriveManager : IDictionary<char, VirtualDrive> {
    private readonly SortedDictionary<char, VirtualDrive> _driveMap = new();
    private readonly Dictionary<char, MemoryDrive> _memoryDriveMap = new();
    private readonly DosMediaIdTable _mediaIdTable;

    /// <summary>
    /// The maximum number of possible DOS drives that can be used.
    /// </summary>
    public const int MaxDriveCount = 26; // A: thru Z:

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

    #region Drive Letter Helpers

    /// <summary>
    /// Gets the zero-based drive index associated with the given DOS drive letter.
    /// </summary>
    /// <param name="value">The DOS drive letter. Valid drive letters are uppercase and lowercase ASCII letters.</param>
    /// <returns>The zero-based drive index associated with the drive letter or -1 if the drive letter is invalid.</returns>
    public static int GetDriveLetterIndex(char value) {
        // Since only ASCII letters are valid here, this could be further optimized by using the "bitwise OR by 0x20"
        // trick to force letters into lowercase, then subtract it by 'a' (into an int), and finally perform an
        // unsigned comparison check to validate that it's in the range [A-Z] or [a-z] to determine whether it should
        // return the subtracted value or -1. That's the optimization that Char.IsAsciiLetter() currently uses.
        // Faster (but less maintainable/readable):
        //   int result = (value | 0x20) - 'a';
        //   return ((uint)result <= 'z' - 'a') ? result : -1;

        if (char.IsBetween(value, 'A', 'Z')) {
            return value - 'A';
        }

        if (char.IsBetween(value, 'a', 'z')) {
            return value - 'a';
        }

        return -1;
    }

    /// <summary>
    /// Gets the zero-based drive index associated with the given DOS drive letter.
    /// </summary>
    /// <param name="value">The DOS drive letter. Valid drive letters are uppercase and lowercase ASCII letters.</param>
    /// <param name="paramName">The parameter name to pass into the <see cref="ArgumentException"/> if <paramref name="value"/> is invalid.</param>
    /// <returns>The zero-based index associated with the drive letter.</returns>
    /// <exception cref="ArgumentException"><paramref name="value"/> is not a valid drive letter.</exception>
    internal static int GetDriveLetterIndexOrThrow(char value, [CallerArgumentExpression(nameof(value))] string? paramName = null) {
        int driveIndex = GetDriveLetterIndex(value);
        if (driveIndex == -1) {
            throw new ArgumentException($"Drive letter '{(!char.IsControl(value) ? value : '?')}' (0x{(int)value:x}) is invalid. It must be an ASCII uppercase or lowercase character between 'A' and 'Z' (inclusive).", paramName);
        }

        Debug.Assert(driveIndex is >= 0 and < MaxDriveCount);
        return driveIndex;
    }

    /// <summary>
    /// Gets the DOS drive letter from a zero-based drive index.
    /// </summary>
    /// <param name="index">Must be a zero-based drive index between 0 (inclusive) and <see cref="MaxDriveCount"/> (exclusive).</param>
    /// <returns>An uppercase ASCII letter representing the drive letter.</returns>
    /// <remarks>
    /// For performance reasons (fast and efficient inlining), this will not throw an <see cref="ArgumentException"/>
    /// if the index is out of range. Thus <paramref name="index"/> must always be validated by the caller prior to
    /// calling this method (and is the reason why it is an internal method).
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static char GetDriveLetterFromIndexFast(int index) {
        Debug.Assert(index is >= 0 and < MaxDriveCount);
        return (char)(index + 'A'); // Only works as long as MaxDriveCount is <= 26.
    }

    /// <summary>
    /// Gets the DOS drive letter from a zero-based drive index.
    /// </summary>
    /// <param name="index">A zero-based drive index between 0 (inclusive) and <see cref="MaxDriveCount"/> (exclusive).</param>
    /// <returns>An uppercase ASCII letter representing the drive letter.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative or greater than or equal to <see cref="MaxDriveCount"/>.</exception>
    public static char GetDriveLetterFromIndex(int index) {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, MaxDriveCount);
        return GetDriveLetterFromIndexFast(index);
    }

    /// <summary>
    /// Attempts to get the DOS drive letter from a zero-based drive index.
    /// </summary>
    /// <param name="index">A zero-based drive index between 0 (inclusive) and <see cref="MaxDriveCount"/> (exclusive).</param>
    /// <param name="driveIndex">If successful, then it is the uppercase ASCII letter representing the drive letter.</param>
    /// <returns><see langword="true"/> if the drive index is valid; otherwise, <see langword="false"/>.</returns>
    public static bool TryGetDriveLetterFromIndex(int index, out char driveLetter) {
        if (index is >= 0 and < MaxDriveCount) {
            driveLetter = GetDriveLetterFromIndexFast(index);
            return true;
        }

        driveLetter = default;
        return false;
    }

    /// <summary>
    /// Validates and normalizes the given drive letter.
    /// </summary>
    /// <param name="value">The DOS drive letter. Valid drive letters are uppercase and lowercase ASCII letters.</param>
    /// <returns>The normalized (uppercase) drive letter.</returns>
    /// <exception cref="ArgumentException"><paramref name="value"/> is not a valid drive letter.</exception>
    public static char NormalizeDriveLetter(char value) {
        // The conversion to an index will validate the char value and the conversion from index to letter will
        // normalize the value to an uppercase drive letter.
        int driveIndex = GetDriveLetterIndexOrThrow(value);
        return GetDriveLetterFromIndexFast(driveIndex);
    }

    /// <summary>
    /// Attempts to validate and normalize the given drive letter.
    /// </summary>
    /// <param name="value">The DOS drive letter. Valid drive letters are uppercase and lowercase ASCII letters.</param>
    /// <param name="normalizedDriveLetter">The normalized (uppercase) drive letter or a default <see cref="char"/> value on failure.</param>
    /// <returns><see langword="true"/> if drive letter is valid and successfully normalized; otherwise, <see langword="false"/>.</returns>
    public static bool TryNormalizeDriveLetter(char value, out char normalizedDriveLetter) {
        // The conversion to an index will validate the char value and the conversion from index to letter will
        // normalize the value to an uppercase drive letter.
        int driveIndex = GetDriveLetterIndex(value);
        if (driveIndex != -1) {
            normalizedDriveLetter = GetDriveLetterFromIndexFast(driveIndex);
            return true;
        }

        normalizedDriveLetter = default;
        return false;
    }

    #endregion

    /// <summary>
    /// The currently selected drive.
    /// </summary>
    public VirtualDrive CurrentDrive { get; set; }

    /// <summary>
    /// Gets the current DOS drive zero based index.
    /// </summary>
    public byte CurrentDriveIndex => (byte)GetDriveLetterFromIndex(CurrentDrive.DriveLetter);

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
