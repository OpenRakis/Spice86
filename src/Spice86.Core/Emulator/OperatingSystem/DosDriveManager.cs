namespace Spice86.Core.Emulator.OperatingSystem;

using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Utils;

using System.Collections;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

/// <summary>
/// The class responsible for centralizing all the mounted DOS drives.
/// </summary>
public class DosDriveManager : IDictionary<char, IVirtualDrive> {
    public Dictionary<char, IVirtualDrive> _driveMap { get; init; } = new Dictionary<char, IVirtualDrive>();

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="cDriveFolderPath">The host path to be mounted as C:.</param>
    /// <param name="executablePath">The host path to the DOS executable to be launched.</param>
    public DosDriveManager(string? cDriveFolderPath, string? executablePath) {
        if (string.IsNullOrWhiteSpace(cDriveFolderPath)) {
            cDriveFolderPath = DosPathResolver.GetExeParentFolder(executablePath);
        }
        cDriveFolderPath = ConvertUtils.ToSlashFolderPath(cDriveFolderPath);
        _driveMap.Add('A', new NullDrive('A', true, "A:"));
        _driveMap.Add('B', new NullDrive('B', true, "B:"));
        _driveMap.Add('C', new MountedFolder('C', cDriveFolderPath));
        CurrentDrive = _driveMap.ElementAt(2).Value;
    }

    public void SetCurrentDrive(ushort zeroBasedDriveIndex) {
        CurrentDrive = _driveMap.ElementAt(zeroBasedDriveIndex).Value;
    }

    public IVirtualDrive CurrentDrive { get; set; }

    /// <summary>
    /// Gets the current DOS drive letter.
    /// </summary>
    public char CurrentDriveLetter => CurrentDrive.DriveLetter;


    internal static readonly FrozenDictionary<char, byte> DriveLetters =
        new Dictionary<char, byte> {
            ['A'] = 0, ['B'] = 1, ['C'] = 2, ['D'] = 3, ['E'] = 4, ['F'] = 5, ['G'] = 6, ['H'] = 7,
            ['I'] = 8, ['J'] = 9, ['K'] = 10, ['L'] = 11, ['M'] = 12, ['N'] = 13, ['O'] = 14, ['P'] = 15,
            ['Q'] = 16, ['R'] = 17, ['S'] = 18, ['T'] = 19, ['U'] = 20, ['V'] = 21, ['W'] = 22, ['X'] = 23,
            ['Y'] = 24, ['Z'] = 25
        }.ToFrozenDictionary();


    /// <summary>
    /// Gets the current DOS drive, as a zero based index.
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


    public ICollection<char> Keys => ((IDictionary<char, IVirtualDrive>)_driveMap).Keys;

    public ICollection<IVirtualDrive> Values => ((IDictionary<char, IVirtualDrive>)_driveMap).Values;

    public int Count => ((ICollection<KeyValuePair<char, IVirtualDrive>>)_driveMap).Count;

    public bool IsReadOnly => ((ICollection<KeyValuePair<char, IVirtualDrive>>)_driveMap).IsReadOnly;

    public IVirtualDrive this[char key] { get => ((IDictionary<char, IVirtualDrive>)_driveMap)[key]; set => ((IDictionary<char, IVirtualDrive>)_driveMap)[key] = value; }


    public const int MaxDriveCount = 26;

    public void Add(char key, IVirtualDrive value) {
        ((IDictionary<char, IVirtualDrive>)_driveMap).Add(key, value);
    }

    public bool ContainsKey(char key) {
        return ((IDictionary<char, IVirtualDrive>)_driveMap).ContainsKey(key);
    }

    public bool Remove(char key) {
        return ((IDictionary<char, IVirtualDrive>)_driveMap).Remove(key);
    }

    public bool TryGetValue(char key, [MaybeNullWhen(false)] out IVirtualDrive value) {
        return ((IDictionary<char, IVirtualDrive>)_driveMap).TryGetValue(key, out value);
    }

    public void Add(KeyValuePair<char, IVirtualDrive> item) {
        ((ICollection<KeyValuePair<char, IVirtualDrive>>)_driveMap).Add(item);
    }

    public void Clear() {
        ((ICollection<KeyValuePair<char, IVirtualDrive>>)_driveMap).Clear();
    }

    public bool Contains(KeyValuePair<char, IVirtualDrive> item) {
        return ((ICollection<KeyValuePair<char, IVirtualDrive>>)_driveMap).Contains(item);
    }

    public void CopyTo(KeyValuePair<char, IVirtualDrive>[] array, int arrayIndex) {
        ((ICollection<KeyValuePair<char, IVirtualDrive>>)_driveMap).CopyTo(array, arrayIndex);
    }

    public bool Remove(KeyValuePair<char, IVirtualDrive> item) {
        return ((ICollection<KeyValuePair<char, IVirtualDrive>>)_driveMap).Remove(item);
    }

    public IEnumerator<KeyValuePair<char, IVirtualDrive>> GetEnumerator() {
        return ((IEnumerable<KeyValuePair<char, IVirtualDrive>>)_driveMap).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() {
        return ((IEnumerable)_driveMap).GetEnumerator();
    }
}
