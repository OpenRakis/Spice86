namespace Spice86.Core.Emulator.OperatingSystem;

using Spice86.Shared.Emulator.Storage;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using ImageBiosParameterBlock = Spice86.Shared.Emulator.Storage.FileSystem.BiosParameterBlock;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

/// <summary>
/// The class responsible for centralizing all the mounted DOS drives.
/// Implements <see cref="IFloppyDriveAccess"/> so the BIOS INT 13h handler can perform
/// low-level sector reads/writes without depending on any DOS-layer types.
/// </summary>
public class DosDriveManager : IDictionary<char, DosDriveBase>, IReadOnlyDictionary<char, DosDriveBase>, IFloppyDriveAccess {
    private readonly DosDriveBase?[] _driveMap = new DosDriveBase?[MaxDriveCount];
    private readonly Dictionary<char, string> _substDriveMap = new();
    private int _mappedDriveCount;
    private uint _version; // Used to prevent simultaneous collection changes and continued enumeration.
    private DriveLetterCollection? _keys;
    private DriveCollection? _values;
    private readonly ILoggerService _loggerService;
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
        _loggerService = loggerService;
        _mediaIdTable = mediaIdTable;
        if (string.IsNullOrWhiteSpace(cDriveFolderPath)) {
            cDriveFolderPath = DosPathResolver.GetExeParentFolder(executablePath);
        }
        cDriveFolderPath = ConvertUtils.ToSlashFolderPath(cDriveFolderPath);
        _driveMap[GetDriveIndex('A')] = new VirtualDrive() { DriveLetter = 'A', MountedHostDirectory = "" };
        _driveMap[GetDriveIndex('B')] = new VirtualDrive() { DriveLetter = 'B', MountedHostDirectory = "" };
        var cDrive = new VirtualDrive { DriveLetter = 'C', MountedHostDirectory = cDriveFolderPath };
        _driveMap[GetDriveIndex('C')] = cDrive;
        CurrentDrive = cDrive;
        _mappedDriveCount = 3; // A:, B:, C:
        InitializeMediaDescriptors();
        if (loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            loggerService.Verbose("DOS Drives initialized: {@Drives}", Values);
        }
    }

    /// <summary>
    /// Gets the zero-based drive index associated with the given DOS drive letter.
    /// </summary>
    /// <param name="driveLetter">The DOS drive letter. Valid drive letters are uppercase and lowercase ASCII letters.</param>
    /// <returns>The zero-based drive index associated with the drive letter or -1 if the drive letter is invalid.</returns>
    public static int GetDriveIndex(char driveLetter) {
        // Since only ASCII letters are valid here, this could be further optimized by using the "bitwise OR by 0x20"
        // trick to force letters into lowercase, then subtract it by 'a' (into an int), and finally perform an
        // unsigned comparison check to validate that it's in the range [A-Z] or [a-z] to determine whether it should
        // return the subtracted value or -1. That's the optimization that Char.IsAsciiLetter() currently uses.
        // Faster (but less maintainable/readable):
        //   int result = (value | 0x20) - 'a';
        //   return ((uint)result <= 'z' - 'a') ? result : -1;

        if (char.IsBetween(driveLetter, 'A', 'Z')) {
            return driveLetter - 'A';
        }

        if (char.IsBetween(driveLetter, 'a', 'z')) {
            return driveLetter - 'a';
        }

        return -1;
    }

    /// <summary>
    /// Gets the zero-based drive index associated with the given DOS drive letter.
    /// </summary>
    /// <param name="driveLetter">The DOS drive letter. Valid drive letters are uppercase and lowercase ASCII letters.</param>
    /// <param name="paramName">The parameter name to pass into the <see cref="ArgumentException"/> if <paramref name="driveLetter"/> is invalid.</param>
    /// <returns>The zero-based index associated with the drive letter.</returns>
    /// <exception cref="ArgumentException"><paramref name="driveLetter"/> is not a valid drive letter.</exception>
    internal static int GetDriveIndexOrThrow(char driveLetter, [CallerArgumentExpression(nameof(driveLetter))] string? paramName = null) {
        int driveIndex = GetDriveIndex(driveLetter);
        if (driveIndex == -1) {
            throw new ArgumentException($"Drive letter '{(!char.IsControl(driveLetter) ? driveLetter : '?')}' (0x{(int)driveLetter:x}) is invalid. It must be an ASCII uppercase or lowercase character between 'A' and 'Z' (inclusive).", paramName);
        }

        Debug.Assert(driveIndex is >= 0 and < MaxDriveCount);
        return driveIndex;
    }

    /// <summary>
    /// Attempts to get the zero-based drive index associated with the given DOS drive letter.
    /// </summary>
    /// <param name="driveLetter">The DOS drive letter. Valid drive letters are uppercase and lowercase ASCII letters.</param>
    /// <param name="driveIndex">The zero-based index associated with the drive letter or -1 on failure.</param>
    /// <returns><see langword="true"/> if the drive letter and associated drive index is valid; otherwise, <see langword="false"/>.</returns>
    public static bool TryGetLetterIndex(char driveLetter, out int driveIndex) {
        driveIndex = GetDriveIndex(driveLetter);
        return driveIndex != -1;
    }

    /// <summary>
    /// Gets the DOS drive letter from a zero-based drive index.
    /// </summary>
    /// <param name="driveIndex">Must be a zero-based drive index between 0 (inclusive) and <see cref="MaxDriveCount"/> (exclusive).</param>
    /// <returns>An uppercase ASCII letter representing the drive letter.</returns>
    /// <remarks>
    /// For performance reasons (fast and efficient inlining), this will not throw an <see cref="ArgumentException"/>
    /// if the index is out of range. Thus <paramref name="driveIndex"/> must always be validated by the caller prior to
    /// calling this method (and is the reason why it is an internal method).
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static char GetDriveLetterFromIndexFast(int driveIndex) {
        Debug.Assert(driveIndex is >= 0 and < MaxDriveCount);
        return (char)(driveIndex + 'A'); // Only works as long as MaxDriveCount is <= 26.
    }

    /// <summary>
    /// Gets the DOS drive letter from a zero-based drive index.
    /// </summary>
    /// <param name="driveIndex">A zero-based drive index between 0 (inclusive) and <see cref="MaxDriveCount"/> (exclusive).</param>
    /// <returns>An uppercase ASCII letter representing the drive letter.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="driveIndex"/> is negative or greater than or equal to <see cref="MaxDriveCount"/>.</exception>
    public static char GetDriveLetterFromIndex(int driveIndex) {
        ArgumentOutOfRangeException.ThrowIfNegative(driveIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(driveIndex, MaxDriveCount);
        return GetDriveLetterFromIndexFast(driveIndex);
    }

    /// <summary>
    /// Attempts to get the DOS drive letter from a zero-based drive index.
    /// </summary>
    /// <param name="driveIndex">A zero-based drive index between 0 (inclusive) and <see cref="MaxDriveCount"/> (exclusive).</param>
    /// <param name="driveIndex">If successful, then it is the uppercase ASCII letter representing the drive letter.</param>
    /// <returns><see langword="true"/> if the drive index is valid; otherwise, <see langword="false"/>.</returns>
    public static bool TryGetDriveLetterFromIndex(int driveIndex, out char driveLetter) {
        if (driveIndex is >= 0 and < MaxDriveCount) {
            driveLetter = GetDriveLetterFromIndexFast(driveIndex);
            return true;
        }

        driveLetter = default;
        return false;
    }

    /// <summary>
    /// Validates and normalizes the given drive letter.
    /// </summary>
    /// <param name="driveLetter">The DOS drive letter. Valid drive letters are uppercase and lowercase ASCII letters.</param>
    /// <returns>The normalized (uppercase) drive letter.</returns>
    /// <exception cref="ArgumentException"><paramref name="driveLetter"/> is not a valid drive letter.</exception>
    public static char NormalizeDriveLetter(char driveLetter) {
        // The conversion to an index will validate the char value and the conversion from index to letter will
        // normalize the value to an uppercase drive letter.
        int driveIndex = GetDriveIndexOrThrow(driveLetter);
        return GetDriveLetterFromIndexFast(driveIndex);
    }

    /// <summary>
    /// Attempts to validate and normalize the given drive letter.
    /// </summary>
    /// <param name="driveLetter">The DOS drive letter. Valid drive letters are uppercase and lowercase ASCII letters.</param>
    /// <param name="normalizedDriveLetter">The normalized (uppercase) drive letter or a default <see cref="char"/> value on failure.</param>
    /// <returns><see langword="true"/> if drive letter is valid and successfully normalized; otherwise, <see langword="false"/>.</returns>
    public static bool TryNormalizeDriveLetter(char driveLetter, out char normalizedDriveLetter) {
        // The conversion to an index will validate the char value and the conversion from index to letter will
        // normalize the value to an uppercase drive letter.
        int driveIndex = GetDriveIndex(driveLetter);
        if (driveIndex != -1) {
            normalizedDriveLetter = GetDriveLetterFromIndexFast(driveIndex);
            return true;
        }

        normalizedDriveLetter = default;
        return false;
    }

    /// <summary>
    /// The currently selected drive.
    /// </summary>
    public VirtualDrive CurrentDrive { get; set; }

    /// <summary>
    /// Gets the current DOS drive zero based index.
    /// </summary>
    public byte CurrentDriveIndex => (byte)GetDriveIndexOrThrow(CurrentDrive.DriveLetter);

    internal bool HasDriveAtIndex(int zeroBasedIndex) => zeroBasedIndex is >= 0 and < MaxDriveCount &&
        _driveMap[zeroBasedIndex] is not null;

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

    /// <summary>
    /// Gets a read only collection of all mapped DOS drive letters in sorted order.
    /// </summary>
    public DriveLetterCollection Keys => _keys ??= new(this);

    ICollection<char> IDictionary<char, DosDriveBase>.Keys => Keys;

    IEnumerable<char> IReadOnlyDictionary<char, DosDriveBase>.Keys => Keys;

    /// <summary>
    /// Gets a read only collection of all mapped DOS drives in sorted order.
    /// </summary>
    public DriveCollection Values => _values ??= new(this);

    ICollection<DosDriveBase> IDictionary<char, DosDriveBase>.Values => Values;

    IEnumerable<DosDriveBase> IReadOnlyDictionary<char, DosDriveBase>.Values => Values;

    bool ICollection<KeyValuePair<char, DosDriveBase>>.IsReadOnly => false;

    /// <summary>
    /// Gets the number of currently mapped DOS drives.
    /// </summary>
    public int Count => _mappedDriveCount;

    /// <summary>
    /// Gets or sets a DOS drive mapping by the drive letter.
    /// </summary>
    /// <param name="key">The drive letter to retrieve. Must be a valid uppercase or lowercase ASCII letter.</param>
    /// <returns>The mapped drive associated with the drive letter.</returns>
    /// <exception cref="KeyNotFoundException">The drive has not been mounted.</exception>
    /// <exception cref="ArgumentException">Setting a drive mapping where <paramref name="key"/> does not match the drive's drive letter.</exception>
    /// <remarks>
    /// This property allows setting the value to <see langword="null"/> to remove a drive letter mapping. If there is
    /// an existing drive mounted at the given location, then it will be disposed before being overwritten.
    /// </remarks>
    [AllowNull]
    public DosDriveBase this[char key] {
        get {
            return _driveMap[GetDriveIndexOrThrow(key)]
                ?? throw new KeyNotFoundException($"Drive '{key}' is not mounted.");
        }

        set {
            if (value is not null && key != value.DriveLetter) {
                throw new ArgumentException("Key must match value's drive letter.");
            }

            int driveIndex = GetDriveIndexOrThrow(key);
            DosDriveBase? existingDrive = _driveMap[driveIndex];
            if (existingDrive is not null && existingDrive != value) {
                // Unmount the existing drive first.
                RemoveDriveInternal(existingDrive, driveIndex);
            }

            _driveMap[driveIndex] = value;
            if (value is not null && existingDrive != value) {
                _mappedDriveCount++;
            }

            // Note that the version will be incremented even if there is no change (value is same instance as existing
            // value). This is to ensure that callers never try to modify the dictionary while continuing to enumerate.
            _version++;
        }
    }

    void IDictionary<char, DosDriveBase>.Add(char key, DosDriveBase value) {
        ArgumentNullException.ThrowIfNull(value);
        if (key != value.DriveLetter) {
            throw new ArgumentException("Key must match drive letter in value.", nameof(key));
        }

        Mount(value);
    }

    public bool ContainsKey(char key) {
        int driveIndex = GetDriveIndex(key);
        if (driveIndex != -1) {
            Debug.Assert(driveIndex is >= 0 and < MaxDriveCount);
            return _driveMap[driveIndex] is not null;
        }

        return false;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// This is a dangerous operation, because the drive will not be disposed before unmounting! Use
    /// <see cref="Unmount(char)"/> or <see cref="UnmountAsync(char)"/> instead.
    /// </remarks>
    bool IDictionary<char, DosDriveBase>.Remove(char key) {
        int driveIndex = GetDriveIndex(key);
        if (driveIndex != -1) {
            Debug.Assert(driveIndex is >= 0 and < MaxDriveCount);
            DosDriveBase? drive = _driveMap[driveIndex];
            if (drive is not null) {
                _driveMap[driveIndex] = null;
                _mappedDriveCount--;
                _version++;
                return true;
            }
        }

        return false;
    }

    public bool TryGetValue(char key, [MaybeNullWhen(false)] out DosDriveBase value) => TryGetDrive(key, out value);

    void ICollection<KeyValuePair<char, DosDriveBase>>.Add(KeyValuePair<char, DosDriveBase> item) {
        ArgumentNullException.ThrowIfNull(item.Value, nameof(item));
        if (item.Key != item.Value.DriveLetter) {
            throw new ArgumentException("Key must match drive letter in value.", nameof(item));
        }

        Mount(item.Value);
    }

    /// <summary>Removes all mounted drives from the collection.</summary>
    /// <remarks>
    /// This will always dispose of the drives before removing them from the collection. (Equivalent to calling
    /// <see cref="Clear(bool)"/> with the dispose drives parameter set to <see langword="true"/>.)
    /// 
    /// This may fail and result in an incomplete drive manager if a drive throws an exception while being disposed.
    /// </remarks>
    public void Clear() => Clear(disposeDrives: true);

    bool ICollection<KeyValuePair<char, DosDriveBase>>.Contains(KeyValuePair<char, DosDriveBase> item) {
        return TryGetDrive(item.Key, out DosDriveBase? value) && value == item.Value;
    }

    public void CopyTo(KeyValuePair<char, DosDriveBase>[] array, int arrayIndex) {
        ArgumentNullException.ThrowIfNull(array);
        ArgumentOutOfRangeException.ThrowIfNegative(arrayIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(arrayIndex, array.Length);

        int itemCount = _mappedDriveCount;
        if (array.Length - arrayIndex < itemCount) {
            throw new ArgumentException("Destination array is not long enough to copy all the items in the collection. Check array index and length.");
        }

        DosDriveBase?[] entries = _driveMap;
        for (int i = 0; i < MaxDriveCount; i++) {
            DosDriveBase? entry = entries[i];
            if (entry is not null) {
                array[arrayIndex++] = new(entry.DriveLetter, entry);
            }
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// This is a dangerous operation, because the drive will not be disposed before unmounting! Use
    /// <see cref="Unmount(char)"/> or <see cref="UnmountAsync(char)"/> instead.
    /// </remarks>
    bool ICollection<KeyValuePair<char, DosDriveBase>>.Remove(KeyValuePair<char, DosDriveBase> item) {
        if (item.Value is null) {
            throw new ArgumentException("Item value must not be null.", nameof(item));
        }

        int driveIndex = GetDriveIndex(item.Key);
        if (driveIndex != -1) {
            Debug.Assert(driveIndex is >= 0 and < MaxDriveCount);
            if (_driveMap[driveIndex] == item.Value) {
                _driveMap[driveIndex] = null;
                _mappedDriveCount--;
                _version++;
                return true;
            }
        }

        return false;
    }

    public Enumerator GetEnumerator() {
        return new Enumerator(this, Enumerator.ReturnTypeKeyValuePair);
    }

    IEnumerator<KeyValuePair<char, DosDriveBase>> IEnumerable<KeyValuePair<char, DosDriveBase>>.GetEnumerator() {
        return new Enumerator(this, Enumerator.ReturnTypeKeyValuePair);
    }

    IEnumerator IEnumerable.GetEnumerator() {
        return new Enumerator(this, Enumerator.ReturnTypeDictionaryEntry);
    }

    public struct Enumerator : IEnumerator<KeyValuePair<char, DosDriveBase>>, IDictionaryEnumerator {
        private readonly DosDriveManager? _dictionary;
        private readonly uint _version; // To make sure MoveNext() fails if dictionary changes while enumerating.
        private int _index; // One-based drive letter index or zero if before start or MaxDriveCount if at end.
        private KeyValuePair<char, DosDriveBase> _current;
        private readonly int _getEnumeratorReturnType;  // What should Enumerator.Current return?

        internal const int ReturnTypeDictionaryEntry = 1;
        internal const int ReturnTypeKeyValuePair = 2;

        internal Enumerator(DosDriveManager dictionary, int getEnumeratorReturnType) {
            Debug.Assert(getEnumeratorReturnType is ReturnTypeDictionaryEntry or ReturnTypeKeyValuePair);
            _dictionary = dictionary;
            _version = dictionary._version;
            _current = default;
            _getEnumeratorReturnType = getEnumeratorReturnType;
        }

        public readonly KeyValuePair<char, DosDriveBase> Current {
            get {
                Debug.Assert(_index is > 0 and <= MaxDriveCount);
                Debug.Assert(_current.Value is not null);
                Debug.Assert(_current.Key == _current.Value.DriveLetter);
                return _current;
            }
        }

        readonly object IEnumerator.Current {
            get {
                ValidateCurrentIndex();

                Debug.Assert(_current.Value is not null);
                Debug.Assert(_current.Key == _current.Value.DriveLetter);

                if (_getEnumeratorReturnType == ReturnTypeDictionaryEntry) {
                    return new DictionaryEntry(_current.Key, _current.Value);
                }

                return _current;
            }
        }

        readonly DictionaryEntry IDictionaryEnumerator.Entry {
            get {
                ValidateCurrentIndex();
                return new(_current.Key, _current.Value);
            }
        }

        readonly object IDictionaryEnumerator.Key {
            get {
                ValidateCurrentIndex();

                Debug.Assert(_current.Value is not null);
                Debug.Assert(_current.Key == _current.Value.DriveLetter);
                return _current.Key;
            }
        }

        readonly object? IDictionaryEnumerator.Value {
            get {
                ValidateCurrentIndex();

                Debug.Assert(_current.Value is not null);
                Debug.Assert(_current.Key == _current.Value.DriveLetter);
                return _current.Value;
            }
        }

        public readonly void Dispose() { }

        public bool MoveNext() {
            if (_dictionary is null) {
                return false;
            }

            ValidateVersion();

            while (_index < MaxDriveCount) {
                DosDriveBase? value = _dictionary._driveMap[_index];
                _index++;

                if (value is not null) {
                    _current = new(value.DriveLetter, value);
                    return true;
                }
            }

            _index = MaxDriveCount + 1;
            _current = default;
            return false;
        }

        public void Reset() {
            _index = 0;
            _current = default;
        }

        [MemberNotNull(nameof(_dictionary))]
        private readonly void ValidateCurrentIndex() {
            if (_index is <= 0 or > MaxDriveCount) {
                throw new InvalidOperationException("Enumeration has either not started or has already finished.");
            }

            Debug.Assert(_dictionary is not null);
        }

        private readonly void ValidateVersion() {
            Debug.Assert(_dictionary is not null);
            if (_version != _dictionary._version) {
                throw new InvalidOperationException("Collection was modified; enumeration operation may not execute.");
            }
        }
    }

    public sealed class DriveLetterCollection(DosDriveManager manager) : ICollection<char>, IReadOnlyCollection<char> {
        private readonly DosDriveManager _dictionary = manager;

        public int Count => _dictionary.Count;

        bool ICollection<char>.IsReadOnly => true;

        void ICollection<char>.Add(char item) {
            throw new NotSupportedException("Mutating a key collection derived from a dictionary is not allowed.");
        }

        void ICollection<char>.Clear() {
            throw new NotSupportedException("Mutating a key collection derived from a dictionary is not allowed.");
        }

        public bool Contains(char item) {
            return _dictionary.ContainsKey(item);
        }

        public void CopyTo(char[] array, int arrayIndex) {
            ArgumentNullException.ThrowIfNull(array);
            ArgumentOutOfRangeException.ThrowIfNegative(arrayIndex);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(arrayIndex, array.Length);
            if (array.Length - arrayIndex < Count) {
                throw new ArgumentException("Destination array is not long enough to copy all the items in the collection. Check array index and length.");
            }

            DosDriveBase?[] entries = _dictionary._driveMap;
            for (int i = 0; i < MaxDriveCount; i++) {
                DosDriveBase? entry = entries[i];
                if (entry is not null) {
                    array[arrayIndex++] = entry.DriveLetter;
                }
            }
        }

        public Enumerator GetEnumerator() {
            return new Enumerator(_dictionary);
        }

        IEnumerator<char> IEnumerable<char>.GetEnumerator() {
            return GetEnumerator();
        }

        bool ICollection<char>.Remove(char item) {
            throw new NotSupportedException("Mutating a key collection derived from a dictionary is not allowed.");
        }

        IEnumerator IEnumerable.GetEnumerator() {
            return GetEnumerator();
        }

        public struct Enumerator : IEnumerator<char> {
            private readonly DosDriveManager? _dictionary;
            private readonly uint _version; // To make sure MoveNext() fails if dictionary changes while enumerating.
            private int _index; // One-based drive letter index or zero if before start or MaxDriveCount if at end.
            private char _current;

            internal Enumerator(DosDriveManager dictionary) {
                _dictionary = dictionary;
                _version = dictionary._version;
                _current = default;
            }

            public readonly char Current {
                get {
                    Debug.Assert(_index is > 0 and <= MaxDriveCount);
                    return _current;
                }
            }

            readonly object IEnumerator.Current {
                get {
                    ValidateCurrentIndex();
                    return _current;
                }
            }

            public readonly void Dispose() { }

            public bool MoveNext() {
                if (_dictionary is null) {
                    return false;
                }

                ValidateVersion();

                while (_index < MaxDriveCount) {
                    DosDriveBase? value = _dictionary._driveMap[_index];
                    _index++;

                    if (value is not null) {
                        _current = value.DriveLetter;
                        return true;
                    }
                }

                _index = MaxDriveCount + 1;
                _current = default;
                return false;
            }

            public void Reset() {
                _index = 0;
                _current = default;
            }

            [MemberNotNull(nameof(_dictionary))]
            private readonly void ValidateCurrentIndex() {
                if (_index is <= 0 or > MaxDriveCount) {
                    throw new InvalidOperationException("Enumeration has either not started or has already finished.");
                }

                Debug.Assert(_dictionary is not null);
            }

            private readonly void ValidateVersion() {
                Debug.Assert(_dictionary is not null);
                if (_version != _dictionary._version) {
                    throw new InvalidOperationException("Collection was modified; enumeration operation may not execute.");
                }
            }
        }
    }

    public sealed class DriveCollection(DosDriveManager manager) : ICollection<DosDriveBase>, IReadOnlyCollection<DosDriveBase> {
        private readonly DosDriveManager _dictionary = manager;

        public int Count => _dictionary.Count;

        bool ICollection<DosDriveBase>.IsReadOnly => true;

        void ICollection<DosDriveBase>.Add(DosDriveBase item) {
            throw new NotSupportedException("Mutating a value collection derived from a dictionary is not allowed.");
        }

        void ICollection<DosDriveBase>.Clear() {
            throw new NotSupportedException("Mutating a value collection derived from a dictionary is not allowed.");
        }

        public bool Contains(DosDriveBase item) {
            return _dictionary.TryGetValue(item.DriveLetter, out DosDriveBase? value) && value == item;
        }

        public void CopyTo(DosDriveBase[] array, int arrayIndex) {
            ArgumentNullException.ThrowIfNull(array);
            ArgumentOutOfRangeException.ThrowIfNegative(arrayIndex);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(arrayIndex, array.Length);
            if (array.Length - arrayIndex < Count) {
                throw new ArgumentException("Destination array is not long enough to copy all the items in the collection. Check array index and length.");
            }

            DosDriveBase?[] entries = _dictionary._driveMap;
            for (int i = 0; i < MaxDriveCount; i++) {
                DosDriveBase? entry = entries[i];
                if (entry is not null) {
                    array[arrayIndex++] = entry;
                }
            }
        }

        public Enumerator GetEnumerator() {
            return new Enumerator(_dictionary);
        }

        IEnumerator<DosDriveBase> IEnumerable<DosDriveBase>.GetEnumerator() {
            return GetEnumerator();
        }

        bool ICollection<DosDriveBase>.Remove(DosDriveBase item) {
            throw new NotSupportedException("Mutating a value collection derived from a dictionary is not allowed.");
        }

        IEnumerator IEnumerable.GetEnumerator() {
            return GetEnumerator();
        }

        public struct Enumerator : IEnumerator<DosDriveBase> {
            private readonly DosDriveManager? _dictionary;
            private readonly uint _version; // To make sure MoveNext() fails if dictionary changes while enumerating.
            private int _index; // One-based drive letter index or zero if before start or MaxDriveCount+1 if at end.
            private DosDriveBase? _current;

            internal Enumerator(DosDriveManager dictionary) {
                _dictionary = dictionary;
                _version = dictionary._version;
                _current = default;
            }

            public readonly DosDriveBase Current {
                get {
                    Debug.Assert(_index is > 0 and <= MaxDriveCount);
                    Debug.Assert(_current is not null);
                    return _current;
                }
            }

            readonly object IEnumerator.Current {
                get {
                    ValidateCurrentIndex();
                    return _current;
                }
            }

            public readonly void Dispose() { }

            public bool MoveNext() {
                if (_dictionary is null) {
                    return false;
                }

                ValidateVersion();

                while (_index < MaxDriveCount) {
                    DosDriveBase? value = _dictionary._driveMap[_index];
                    _index++;

                    if (value is not null) {
                        _current = value;
                        return true;
                    }
                }

                _index = MaxDriveCount + 1;
                _current = null;
                return false;
            }

            public void Reset() {
                _index = 0;
                _current = default;
            }

            [MemberNotNull(nameof(_dictionary), nameof(_current))]
            private readonly void ValidateCurrentIndex() {
                if (_index is <= 0 or > MaxDriveCount) {
                    throw new InvalidOperationException("Enumeration has either not started or has already finished.");
                }

                Debug.Assert(_dictionary is not null);
                Debug.Assert(_current is not null);
            }

            private readonly void ValidateVersion() {
                Debug.Assert(_dictionary is not null);
                if (_version != _dictionary._version) {
                    throw new InvalidOperationException("Collection was modified; enumeration operation may not execute.");
                }
            }
        }
    }

    /// <summary>Removes all mounted drives from the collection.</summary>
    /// <param name="disposeDrives">
    /// If <see langword="true"/>, then all currently mounted drives will be disposed (if applicable). If
    /// <see langword="false"/>, then drives will only be removed from the collection and will not be disposed.
    /// </param>
    /// <remarks>
    /// Removing drives from this collection without disposing may result in unexpected behavior or memory leaks. It is
    /// up to the caller to make sure that any drives that are currently mounted are disposed before clearing the
    /// collection.
    /// 
    /// This may fail and result in an incomplete drive manager if a drive throws an exception while being disposed.
    /// </remarks>
    public void Clear(bool disposeDrives) {
        if (!disposeDrives) {
            Array.Clear(_driveMap);
            _mappedDriveCount = 0;
            _version++;
            return;
        }

        // Keep track of exceptions that occur while removing drives.
        for (int i = 0; i < MaxDriveCount; i++) {
            DosDriveBase? drive = _driveMap[i];
            if (drive is not null) {
                RemoveDriveInternal(drive, i);
            }
        }

        Debug.Assert(_mappedDriveCount == 0);

        // Always increment the version, even if no drives were unmounted.
        _version++;
    }

    /// <summary>
    /// Mounts a generic DOS drive.
    /// </summary>
    /// <param name="drive">The DOS drive to mount.</param>
    /// <exception cref="InvalidOperationException">A DOS drive with the same drive letter has already been mounted.</exception>
    public void Mount(DosDriveBase drive) {
        int driveIndex = GetDriveIndexOrThrow(drive.DriveLetter, nameof(drive));
        if (_driveMap[driveIndex] is not null) {
            throw new InvalidOperationException($"A DOS drive with the same drive letter '{drive.DriveLetter}' has already been mounted.");
        }

        _driveMap[driveIndex] = drive;
        _mappedDriveCount++;
        _version++;
    }

    /// <summary>
    /// Unmounts the DOS drive with the specified drive letter.
    /// </summary>
    /// <param name="driveLetter">The DOS drive letter. Valid drive letters are uppercase and lowercase ASCII letters.</param>
    /// <exception cref="InvalidOperationException">Drive is not mounted.</exception>
    public void Unmount(char driveLetter) {
        int driveIndex = GetDriveIndexOrThrow(driveLetter);
        Debug.Assert(driveIndex is >= 0 and < MaxDriveCount);
        DosDriveBase? drive = _driveMap[driveIndex]
            ?? throw new InvalidOperationException($"No DOS drive has been mounted with the drive letter '{driveLetter}'.");
        RemoveDriveInternal(drive, driveIndex);
    }

    /// <summary>
    /// Asynchronously unmounts the DOS drive with the specified drive letter.
    /// </summary>
    /// <param name="driveLetter">The DOS drive letter. Valid drive letters are uppercase and lowercase ASCII letters.</param>
    /// <returns>An asynchronous task which completes when the mounted drive has been disposed and unmounted.</returns>
    /// <exception cref="InvalidOperationException">Drive is not mounted.</exception>
    /// <remarks>
    /// This only performs an asynchronous non-blocking operation if the mounted drive implemented
    /// <see cref="IAsyncDisposable"/>. If the drive implements <see cref="IDisposable"/>, but not
    /// <see cref="IAsyncDisposable"/>, then this method will synchronously block until the drive has been disposed.
    /// Avoid using the drive letter for any other operations until the asynchronous task completes.
    /// </remarks>
    public ValueTask UnmountAsync(char driveLetter) {
        int driveIndex = GetDriveIndexOrThrow(driveLetter);
        Debug.Assert(driveIndex is >= 0 and < MaxDriveCount);
        DosDriveBase? drive = _driveMap[driveIndex]
            ?? throw new InvalidOperationException($"No DOS drive has been mounted with the drive letter '{driveLetter}'.");
        return RemoveDriveInternalAsync(drive, driveIndex);
    }

    private void RemoveDriveInternal(DosDriveBase drive, int driveIndex) {
        Debug.Assert(driveIndex is >= 0 and < MaxDriveCount);
        try {
            // Dispose of the drive, if possible, before unmounting.
            if (drive is IDisposable disposable) {
                disposable.Dispose();
            } else if (drive is IAsyncDisposable asyncDisposable) {
                ValueTask valueTask = asyncDisposable.DisposeAsync();
                if (!valueTask.IsCompletedSuccessfully) {
                    valueTask.AsTask().Wait();
                }
            }
        } finally {
            _driveMap[driveIndex] = null;
            _mappedDriveCount--;
            _version++;
        }
    }

    private async ValueTask RemoveDriveInternalAsync(DosDriveBase drive, int driveIndex) {
        Debug.Assert(driveIndex is >= 0 and < MaxDriveCount);
        try {
            // Dispose of the drive, if possible, before unmounting.
            if (drive is IAsyncDisposable asyncDisposable) {
                await asyncDisposable.DisposeAsync();
            } else if (drive is IDisposable disposable) {
                disposable.Dispose();
            }
        } finally {
            _driveMap[driveIndex] = null;
            _mappedDriveCount--;
            _version++;
        }
    }

    public DosDriveBase GetDrive(char driveLetter) => TryGetDrive(driveLetter, out DosDriveBase? drive)
        ? drive : throw new KeyNotFoundException($"Drive '{driveLetter}' is not mounted.");

    public T GetDrive<T>(char driveLetter) where T : DosDriveBase => TryGetDrive(driveLetter, out T? drive)
        ? drive : throw new KeyNotFoundException($"Drive '{driveLetter}' is not mounted.");

    /// <summary>
    /// Attempts to get a DOS drive mounted using the given DOS drive letter.
    /// </summary>
    /// <param name="driveLetter">The DOS drive letter. Valid drive letters are uppercase and lowercase ASCII letters.</param>
    /// <param name="drive">The mounted drive if found; otherwise, <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if a drive exists with the given DOS drive letter; otherwise, <see langword="false"/>.</returns>
    public bool TryGetDrive(char driveLetter, [MaybeNullWhen(false)] out DosDriveBase drive) {
        int driveIndex = GetDriveIndex(driveLetter);
        if (driveIndex != -1) {
            Debug.Assert(driveIndex is >= 0 and < MaxDriveCount);
            DosDriveBase? mountedDrive = _driveMap[driveIndex];
            if (mountedDrive is not null) {
                drive = mountedDrive;
                return true;
            }
        }

        drive = null;
        return false;
    }

    /// <summary>
    /// Attempts to get a DOS drive of a specific type mounted using the given DOS drive letter.
    /// </summary>
    /// <typeparam name="T">The type of DOS drive object to retrieve.</typeparam>
    /// <param name="driveLetter">The DOS drive letter. Valid drive letters are uppercase and lowercase ASCII letters.</param>
    /// <param name="drive">The mounted drive of the specified type if found; otherwise, <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if a drive of the specified type exists with the given DOS drive letter; otherwise, <see langword="false"/>.</returns>
    public bool TryGetDrive<T>(char driveLetter, [NotNullWhen(true)] out T? drive) where T : DosDriveBase {
        int driveIndex = GetDriveIndex(driveLetter);
        if (driveIndex != -1) {
            Debug.Assert(driveIndex is >= 0 and < MaxDriveCount);
            DosDriveBase? mountedDrive = _driveMap[driveIndex];
            if (mountedDrive is T mountedDriveType) {
                drive = mountedDriveType;
                return true;
            }
        }

        drive = null;
        return false;
    }

    public DosDriveBase GetDriveAtIndex(int driveIndex) => TryGetDriveAtIndex(driveIndex, out DosDriveBase? drive)
        ? drive : throw new KeyNotFoundException($"Drive at index {driveIndex} is not mounted.");

    public T GetDriveAtIndex<T>(int driveIndex) where T : DosDriveBase => TryGetDriveAtIndex(driveIndex, out T? drive)
        ? drive : throw new KeyNotFoundException($"Drive at index {driveIndex} is not mounted.");

    /// <summary>
    /// Attempts to get the DOS drive mounted at the given DOS drive index.
    /// </summary>
    /// <param name="driveIndex">A zero-based drive index between 0 (inclusive) and <see cref="MaxDriveCount"/> (exclusive).</param>
    /// <param name="value">The mounted drive if found; otherwise, <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if a drive exists with the given DOS drive letter; otherwise, <see langword="false"/>.</returns>
    public bool TryGetDriveAtIndex(int driveIndex, [MaybeNullWhen(false)] out DosDriveBase value) {
        if (driveIndex is >= 0 and < MaxDriveCount) {
            DosDriveBase? mountedDrive = _driveMap[driveIndex];
            if (mountedDrive is not null) {
                value = mountedDrive;
                return true;
            }
        }

        value = null;
        return false;
    }

    /// <summary>
    /// Attempts to get the DOS drive mounted at the given DOS drive index.
    /// </summary>
    /// <typeparam name="T">The type of DOS drive object to retrieve.</typeparam>
    /// <param name="driveIndex">A zero-based drive index between 0 (inclusive) and <see cref="MaxDriveCount"/> (exclusive).</param>
    /// <param name="value">The mounted drive of the specified type if found; otherwise, <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if a drive of the specified type exists with the given DOS drive letter; otherwise, <see langword="false"/>.</returns>
    public bool TryGetDriveAtIndex<T>(int driveIndex, [NotNullWhen(true)] out T? value) where T : DosDriveBase {
        if (driveIndex is >= 0 and < MaxDriveCount) {
            DosDriveBase? mountedDrive = _driveMap[driveIndex];
            if (mountedDrive is T mountedDriveType) {
                value = mountedDriveType;
                return true;
            }
        }

        value = null;
        return false;
    }

    /// <summary>
    /// Mounts a memory-backed drive (typically Z: for AUTOEXEC.BAT).
    /// </summary>
    /// <param name="drive">The memory drive to mount.</param>
    public void MountMemoryDrive(MemoryDrive drive) => Mount(drive);

    /// <summary>
    /// Tries to get a mounted memory drive by letter.
    /// </summary>
    /// <param name="driveLetter">The drive letter (e.g., 'Z').</param>
    /// <param name="drive">The memory drive if found; null otherwise.</param>
    /// <returns>True if memory drive exists; false otherwise.</returns>
    public bool TryGetMemoryDrive(char driveLetter, [MaybeNullWhen(false)] out MemoryDrive drive) {
        return TryGetDrive(driveLetter, out drive);
    }

    /// <summary>
    /// Gets a read-only view of all mounted memory drives, keyed by drive letter.
    /// </summary>
    public IEnumerable<KeyValuePair<char, MemoryDrive>> MemoryDrives {
        get {
            for (int i = 0; i < MaxDriveCount; i++) {
                if (_driveMap[i] is MemoryDrive m) {
                    yield return new KeyValuePair<char, MemoryDrive>(m.DriveLetter, m);
                }
            }
        }
    }

    /// <summary>
    /// Gets a read-only view of all floppy drives with an image mounted, keyed by drive letter.
    /// </summary>
    public IEnumerable<KeyValuePair<char, FloppyDiskDrive>> FloppyDrives {
        get {
            for (int i = 0; i < MaxDriveCount; i++) {
                if (_driveMap[i] is FloppyDiskDrive f && f.Image != null) {
                    yield return new KeyValuePair<char, FloppyDiskDrive>(f.DriveLetter, f);
                }
            }
        }
    }

    /// <summary>
    /// Tries to get a floppy drive by letter, returning <see langword="true"/> only when an image is mounted.
    /// </summary>
    /// <param name="driveLetter">The drive letter to look up.</param>
    /// <param name="drive">The floppy drive if found; null otherwise.</param>
    /// <returns>True if a floppy image is mounted on the specified letter; false otherwise.</returns>
    public bool TryGetFloppyDrive(char driveLetter, [MaybeNullWhen(false)] out FloppyDiskDrive drive) {
        if (TryGetDrive(driveLetter, out FloppyDiskDrive? f) && f.Image != null) {
            drive = f;
            return true;
        }
        drive = null;
        return false;
    }

    /// <summary>
    /// Flushes all dirty floppy disk images back to their backing host files.
    /// </summary>
    /// <remarks>
    /// Mirrors the on-pause / on-exit write-back behaviour of dosbox-staging
    /// (see <c>src/dos/drive_fat.cpp</c>): every floppy whose image has been
    /// modified by an INT 13h / INT 26h sector write is rewritten to disk and
    /// its dirty flag is cleared on success.
    /// </remarks>
    /// <returns>The number of floppy drives whose image was actually flushed.</returns>
    public int FlushDirtyFloppyImages() {
        int flushedCount = 0;
        for (int i = 0; i < _driveMap.Length; i++) {
            if (_driveMap[i] is not FloppyDiskDrive floppy) {
                continue;
            }
            if (!floppy.HasDirtyImages) {
                continue;
            }
            int flushedImageCount = floppy.FlushDirtyImagesToDisk();
            if (flushedImageCount > 0) {
                flushedCount++;
            }
        }
        return flushedCount;
    }

    /// <summary>
    /// Mounts a floppy disk image (raw FAT12 bytes) to the specified drive letter (A: or B:).
    /// Replaces any existing drive at that letter.
    /// </summary>
    public void MountFloppyImage(char driveLetter, byte[] imageData, string imagePath) {
        char upper = NormalizeDriveLetter(driveLetter);
        FloppyDiskDrive floppy = new() { DriveLetter = upper };
        floppy.MountImage(imageData, imagePath);
        ReplaceDrive(upper, floppy);
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _loggerService.Information("IMGMOUNT: Mounted image {Image} on drive {Drive}:", imagePath, upper);
        }
    }

    /// <summary>
    /// Adds an additional floppy disk image to an already-mounted floppy drive,
    /// making it available for Ctrl-F4 disc switching. If no floppy drive is currently
    /// mounted on the letter, a new drive is created with this as the first image.
    /// </summary>
    public void AddFloppyImage(char driveLetter, byte[] imageData, string imagePath) {
        char upper = NormalizeDriveLetter(driveLetter);
        if (!TryGetDrive(upper, out FloppyDiskDrive? floppy)) {
            floppy = new FloppyDiskDrive { DriveLetter = upper };
            floppy.MountImage(imageData, imagePath);
            ReplaceDrive(upper, floppy);
        } else {
            floppy.AddImage(imageData, imagePath);
        }
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _loggerService.Information("IMGMOUNT: Added image {Image} to drive {Drive}: ({Count} total)", imagePath, upper, floppy.ImageCount);
        }
    }

    /// <summary>
    /// Advances every floppy drive that has more than one image to the next image in its list.
    /// </summary>
    public void SwapFloppyDiscs() {
        for (int i = 0; i < MaxDriveCount; i++) {
            if (_driveMap[i] is FloppyDiskDrive floppy && floppy.Image != null) {
                floppy.SwapToNextImage();
                if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
                    _loggerService.Information("MOUNT: Swapping drive {Drive}: to image {Image}", floppy.DriveLetter, floppy.ImagePath);
                }
            }
        }
    }

    /// <summary>Switches the floppy drive at <paramref name="letter"/> to the image at <paramref name="index"/>.</summary>
    public void SwapFloppyToIndex(char letter, int index) {
        char upper = NormalizeDriveLetter(letter);
        if (!TryGetDrive(upper, out FloppyDiskDrive? drive)) {
            return;
        }
        drive.SwapToIndex(index);
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _loggerService.Information("MOUNT: Drive {Drive}: switched to image {Image}", upper, drive.ImagePath);
        }
    }

    /// <summary>
    /// Mounts a host folder as a folder-backed floppy drive (A: or B:).
    /// </summary>
    public void MountFloppyFolder(char driveLetter, string hostFolderPath) {
        char upper = NormalizeDriveLetter(driveLetter);
        ReplaceDrive(upper, new VirtualDrive {
            DriveLetter = upper,
            MountedHostDirectory = ConvertUtils.ToSlashFolderPath(hostFolderPath),
        });
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Debug)) {
            _loggerService.Debug("DosDriveManager: mounted folder {Path} as {Drive}:", hostFolderPath, upper);
        }
    }

    /// <summary>
    /// Mounts a host folder as a regular (HDD-style) DOS drive.
    /// Adds the drive if it does not already exist, or replaces the existing entry.
    /// </summary>
    public void MountFolderDrive(char driveLetter, string hostFolderPath) {
        char upper = NormalizeDriveLetter(driveLetter);
        VirtualDrive newDrive = new VirtualDrive {
            DriveLetter = upper,
            MountedHostDirectory = ConvertUtils.ToSlashFolderPath(hostFolderPath),
        };
        ReplaceDrive(upper, newDrive);
        if (CurrentDrive.DriveLetter == upper) {
            CurrentDrive = newDrive;
        }
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _loggerService.Information("MOUNT: Drive {Drive}: is now backed by folder {Path}", upper, hostFolderPath);
        }
    }

    /// <summary>
    /// Registers a drive letter for a CD-ROM drive so that drive-change commands
    /// (e.g. <c>D:</c>) succeed after <c>IMGMOUNT</c> or <c>MOUNT -t cdrom</c>.
    /// </summary>
    public void RegisterCdRomDriveLetter(char driveLetter, string hostFolderPath) {
        char upper = NormalizeDriveLetter(driveLetter);
        string mountPath = string.IsNullOrEmpty(hostFolderPath)
            ? string.Empty
            : ConvertUtils.ToSlashFolderPath(hostFolderPath);
        ReplaceDrive(upper, new VirtualDrive {
            DriveLetter = upper,
            MountedHostDirectory = mountPath,
        });
    }

    /// <summary>
    /// Mounts a host folder as a SUBST drive.
    /// </summary>
    public void MountSubstDrive(char driveLetter, string hostFolderPath, string originalDosPath) {
        char upper = NormalizeDriveLetter(driveLetter);
        MountFolderDrive(upper, hostFolderPath);
        _substDriveMap[upper] = originalDosPath;
    }

    /// <summary>
    /// Removes a previously-SUBST'd drive. Returns <c>false</c> when the drive letter is not currently SUBST'd.
    /// </summary>
    public bool UnmountSubstDrive(char driveLetter) {
        char upper = NormalizeDriveLetter(driveLetter);
        if (!_substDriveMap.Remove(upper)) {
            return false;
        }
        int idx = GetDriveIndex(upper);
        if (idx >= 0 && _driveMap[idx] is VirtualDrive drive) {
            RemoveDriveInternal(drive, idx);
            if (CurrentDrive.DriveLetter == upper && TryGetDrive('C', out VirtualDrive? cDrive)) {
                CurrentDrive = cDrive;
            }
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
                _loggerService.Information("SUBST: Drive {Drive}: removed (was {Path})", upper, drive.MountedHostDirectory);
            }
        }
        return true;
    }

    /// <summary>Gets the SUBST drive map (drive letter -> original DOS path).</summary>
    public IReadOnlyDictionary<char, string> SubstDrives => _substDriveMap;

    /// <summary>Returns <c>true</c> when <paramref name="driveLetter"/> currently refers to a SUBST drive.</summary>
    public bool IsSubstDrive(char driveLetter) =>
        _substDriveMap.ContainsKey(NormalizeDriveLetter(driveLetter));

    /// <inheritdoc/>
    public bool TryGetGeometry(byte driveNumber, out int totalCylinders, out int headsPerCylinder, out int sectorsPerTrack, out int bytesPerSector) {
        totalCylinders = 0;
        headsPerCylinder = 0;
        sectorsPerTrack = 0;
        bytesPerSector = 0;

        if (!TryResolveImageBackedDrive(driveNumber, out FloppyDiskDrive? _, out byte[]? imageData) || imageData == null) {
            return false;
        }

        ImageBiosParameterBlock bpb = ParseBpb(imageData);
        bytesPerSector = bpb.BytesPerSector;
        sectorsPerTrack = bpb.SectorsPerTrack;
        headsPerCylinder = bpb.NumberOfHeads;
        int totalSectors = bpb.TotalSectors;
        if (sectorsPerTrack > 0 && headsPerCylinder > 0) {
            totalCylinders = totalSectors / (sectorsPerTrack * headsPerCylinder);
        }
        return true;
    }

    /// <inheritdoc/>
    public bool ReadFromImage(byte driveNumber, int imageByteOffset, byte[] destination, int destOffset, int byteCount) {
        if (!TryResolveImageBackedDrive(driveNumber, out FloppyDiskDrive? _, out byte[]? imageData)) {
            return false;
        }
        if (imageByteOffset < 0 || imageByteOffset + byteCount > imageData!.Length) {
            return false;
        }
        imageData.AsSpan(imageByteOffset, byteCount).CopyTo(destination.AsSpan(destOffset));
        return true;
    }

    /// <inheritdoc/>
    public bool WriteToImage(byte driveNumber, int imageByteOffset, byte[] source, int srcOffset, int byteCount) {
        if (!TryResolveImageBackedDrive(driveNumber, out FloppyDiskDrive? floppy, out byte[]? imageData)) {
            return false;
        }
        if (imageByteOffset < 0 || imageByteOffset + byteCount > imageData!.Length) {
            return false;
        }
        source.AsSpan(srcOffset, byteCount).CopyTo(imageData.AsSpan(imageByteOffset));
        floppy?.MarkDirty();
        return true;
    }

    private void ReplaceDrive(char driveLetter, DosDriveBase newDrive) {
        int idx = GetDriveIndexOrThrow(driveLetter);
        DosDriveBase? existing = _driveMap[idx];
        if (existing is not null) {
            RemoveDriveInternal(existing, idx);
        }
        _driveMap[idx] = newDrive;
        _mappedDriveCount++;
        _version++;
    }

    private bool TryResolveImageBackedDrive(byte driveNumber, out FloppyDiskDrive? floppy, out byte[]? imageData) {
        floppy = null;
        imageData = null;
        char driveLetter = (char)('A' + driveNumber);
        if (driveLetter < 'A' || driveLetter > 'Z') {
            return false;
        }
        if (!TryGetFloppyDrive(driveLetter, out floppy)) {
            return false;
        }
        if (floppy.Image == null) {
            return false;
        }
        imageData = floppy.GetCurrentImageData();
        return imageData != null;
    }

    private static ImageBiosParameterBlock ParseBpb(byte[] imageData) {
        return ImageBiosParameterBlock.Parse(imageData.AsSpan(0, Math.Min(512, imageData.Length)));
    }
}
