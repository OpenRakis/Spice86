using Microsoft.Extensions.Logging;

namespace Spice86.Core.Emulator.OperatingSystem;

using Spice86.Core.Emulator.Devices.CdRom;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.InterruptHandlers.Mscdex;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Emulator.Storage;
using Spice86.Shared.Emulator.Storage.CdRom;
using Spice86.Shared.Emulator.Storage.FileSystem;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

using FatBiosParameterBlock = Spice86.Shared.Emulator.Storage.FileSystem.FatBiosParameterBlock;

/// <summary>
/// The class responsible for centralizing all the mounted DOS drives.
/// Implements (among other interfaces) <see cref="IFloppyDriveAccess"/> so the BIOS INT 13h handler can perform
/// low-level sector reads/writes without depending on any DOS-layer types.
/// </summary>
public class DosDriveManager : IDictionary<char, DosDriveBase>, IReadOnlyDictionary<char, DosDriveBase>, IFloppyDriveAccess, IDriveStatusProvider, IDiscSwapper, IDriveMountService, IDriveContentMapProvider, IDriveFileListProvider {
    /// <summary>
    /// The maximum number of possible DOS drives that can be used.
    /// </summary>
    public const int MaxDriveCount = 26;

    internal const char AltDirectorySeparatorChar = '/';
    internal const char DirectorySeparatorChar = '\\';
    internal const char VolumeSeparatorChar = ':';
    private const int CookedCdSectorSize = 2048;
    private const byte DefaultCdDriveIndex = 3;
    private const int DosExtlength = 3;
    private const int DosMfnlength = 8;
    private const byte FixedDiskMediaDescriptor = 0xF8;
    private const byte FloppyMediaDescriptor = 0xF0;
    private const int LfnNamelength = 255;
    private const int MaxPathLength = 255;
    private const int MaxVisualizationClusters = 4096;

    // for the sanity check only
    // Match DOS COMMAND.COM batch-first executable lookup order: .BAT is searched before .COM and .EXE.
    private static readonly string[] ExecutableExtensionLookupOrder = [".BAT", ".COM", ".EXE"];

    private readonly IDriveActivityNotifier _activityNotifier;
    private readonly ISoundChannelCreator _channelCreator;
    private readonly DosDriveBase?[] _driveMap = new DosDriveBase?[MaxDriveCount];
    private readonly ILogger _loggerService;
    private readonly DosMediaIdTable _mediaIdTable;
    private readonly Mscdex _mscdex;
    private readonly Dictionary<char, string> _substDriveMap = new();
    private DriveLetterCollection? _keys;
    private int _mappedDriveCount;
    private DriveCollection? _values;
    private uint _version; // Used to prevent simultaneous collection changes and continued enumeration.
                           // A: thru Z:

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="mscdex">The MSCDEX driver for accessing CD drives.</param>
    /// <param name="channelCreator">The sound channel creator, used to stream CD audio when an image is mounted.</param>
    /// <param name="activityNotifier">Notifier that surfaces per-drive read/write activity to the UI.</param>
    /// <param name="loggerService">The service used to log messages.</param>
    /// <param name="cDriveFolderPath">The host path to be mounted as C:.</param>
    /// <param name="executablePath">The host path to the DOS executable to be launched.</param>
    /// <param name="mediaIdTable">The DOS private-segment media ID table owned by this manager.</param>
    public DosDriveManager(Mscdex mscdex,
        ISoundChannelCreator channelCreator,
        IDriveActivityNotifier activityNotifier,
        string? cDriveFolderPath, string? executablePath, DosMediaIdTable mediaIdTable, ILogger loggerService) {
        _mscdex = mscdex;
        _activityNotifier = activityNotifier;
        _loggerService = loggerService;
        _mediaIdTable = mediaIdTable;
        _channelCreator = channelCreator;
        if (string.IsNullOrWhiteSpace(cDriveFolderPath)) {
            cDriveFolderPath = GetExeParentFolder(executablePath);
        }
        cDriveFolderPath = ConvertUtils.ToSlashFolderPath(cDriveFolderPath);
        _driveMap[GetDriveIndex('A')] = new EmptyDosDrive('A');
        _driveMap[GetDriveIndex('B')] = new EmptyDosDrive('B');
        FolderDrive cDrive = new FolderDrive { DriveLetter = 'C', MountedHostDirectory = cDriveFolderPath };
        _driveMap[GetDriveIndex('C')] = cDrive;
        CurrentDrive = cDrive;
        _mappedDriveCount = 3; // A:, B:, C:
        InitializeMediaDescriptors();
        if (loggerService.IsEnabled(LogLevel.Trace)) {
            loggerService.LogTrace("DOS Drives initialized: {@Drives}", Values);
        }
    }

    /// <summary>
    /// Gets the number of currently mapped DOS drives.
    /// </summary>
    public int Count => _mappedDriveCount;

    /// <summary>
    /// The currently selected drive.
    /// </summary>
    public DosDriveBase CurrentDrive { get; set; }

    /// <summary>
    /// Gets the current DOS drive zero based index.
    /// </summary>
    public byte CurrentDriveIndex => (byte)GetDriveIndexOrThrow(CurrentDrive.DriveLetter);

    /// <summary>
    /// Gets a read-only view of all floppy drives with an image mounted, keyed by drive letter.
    /// </summary>
    public IEnumerable<KeyValuePair<char, FloppyDiskDrive>> FloppyDrives {
        get {
            for (int i = 0; i < MaxDriveCount; i++) {
                if (_driveMap[i] is FloppyDiskDrive f && f.HasImage) {
                    yield return new KeyValuePair<char, FloppyDiskDrive>(f.DriveLetter, f);
                }
            }
        }
    }

    bool ICollection<KeyValuePair<char, DosDriveBase>>.IsReadOnly => false;

    /// <summary>
    /// Gets a read only collection of all mapped DOS drive letters in sorted order.
    /// </summary>
    public DriveLetterCollection Keys => _keys ??= new(this);

    ICollection<char> IDictionary<char, DosDriveBase>.Keys => Keys;

    IEnumerable<char> IReadOnlyDictionary<char, DosDriveBase>.Keys => Keys;

    /// <summary>The segment of the media ID table, used as DS in AH=1Bh/1Ch returns.</summary>
    public ushort MediaIdTableSegment => _mediaIdTable.Segment;

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

    /// <summary>Gets the SUBST drive map (drive letter -> original DOS path).</summary>
    public IReadOnlyDictionary<char, string> SubstDrives => _substDriveMap;

    /// <summary>
    /// Gets a read only collection of all mapped DOS drives in sorted order.
    /// </summary>
    public DriveCollection Values => _values ??= new(this);

    ICollection<DosDriveBase> IDictionary<char, DosDriveBase>.Values => Values;

    IEnumerable<DosDriveBase> IReadOnlyDictionary<char, DosDriveBase>.Values => Values;

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

    public static bool WildFileCmp(string? filename, string? pattern) {
        if (filename is null || pattern is null) {
            return false;
        }

        return WildFileCmp(filename.AsSpan(), pattern.AsSpan());
    }

    void IDictionary<char, DosDriveBase>.Add(char key, DosDriveBase value) {
        ArgumentNullException.ThrowIfNull(value);
        if (key != value.DriveLetter) {
            throw new ArgumentException("Key must match drive letter in value.", nameof(key));
        }

        Mount(value);
    }

    void ICollection<KeyValuePair<char, DosDriveBase>>.Add(KeyValuePair<char, DosDriveBase> item) {
        ArgumentNullException.ThrowIfNull(item.Value, nameof(item));
        if (item.Key != item.Value.DriveLetter) {
            throw new ArgumentException("Key must match drive letter in value.", nameof(item));
        }

        Mount(item.Value);
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
        if (_loggerService.IsEnabled(LogLevel.Information)) {
            _loggerService.LogInformation("IMGMOUNT: Added image {Image} to drive {Drive}: ({Count} total)", imagePath, upper, floppy.ImageCount);
        }
    }

    /// <summary>
    /// Returns whether the folder or file name already exists, in DOS's case insensitive point of view.
    /// </summary>
    /// <param name="newFileOrDirectoryPath">The name of new file or folder we try to create.</param>
    /// <param name="hostFolder">The full path to the host folder to look into.</param>
    /// <returns>A boolean value indicating if there is any folder or file with the same name.</returns>
    public bool AnyDosDirectoryOrFileWithTheSameName(string newFileOrDirectoryPath, DirectoryInfo hostFolder) =>
        GetTopLevelDirsAndFiles(hostFolder.FullName).Any(x =>
            string.Equals(Path.GetFileName(x), Path.GetFileName(newFileOrDirectoryPath), StringComparison.OrdinalIgnoreCase));

    /// <summary>Removes all mounted drives from the collection.</summary>
    /// <remarks>
    /// This will always dispose of the drives before removing them from the collection. (Equivalent to calling
    /// <see cref="Clear(bool)"/> with the dispose drives parameter set to <see langword="true"/>.)
    ///
    /// This may fail and result in an incomplete drive manager if a drive throws an exception while being disposed.
    /// </remarks>
    public void Clear() => Clear(disposeDrives: true);

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

    bool ICollection<KeyValuePair<char, DosDriveBase>>.Contains(KeyValuePair<char, DosDriveBase> item) {
        return TryGetDrive(item.Key, out DosDriveBase? value) && value == item.Value;
    }

    public bool ContainsKey(char key) {
        int driveIndex = GetDriveIndex(key);
        if (driveIndex != -1) {
            Debug.Assert(driveIndex is >= 0 and < MaxDriveCount);
            return _driveMap[driveIndex] is not null;
        }

        return false;
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

    public IEnumerable<string> FindFilesUsingWildCmp(string searchFolder, string searchPattern,
            EnumerationOptions enumerationOptions) {
        return Directory.EnumerateFileSystemEntries(searchFolder, "*", enumerationOptions)
            .Where(path => WildFileCmp(Path.GetFileName(path), searchPattern));
    }

    /// <summary>
    /// Flushes all dirty floppy disk images back to their backing host files.
    /// </summary>
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

    /// <inheritdoc />
    public DriveContentMap GetContentMap(char driveLetter) {
        char upper = char.ToUpperInvariant(driveLetter);
        if (TryGetFloppyDrive(upper, out FloppyDiskDrive? floppy) && floppy.Image != null) {
            DriveClusterState[] states = floppy.Image.GetClusterUsageBitmap(MaxVisualizationClusters);
            List<DriveClusterInfo> clusterInfos = new(states.Length);
            for (int i = 0; i < states.Length; i++) {
                clusterInfos.Add(new DriveClusterInfo(i, states[i]));
            }
            int totalClusters = floppy.Image.Bpb.SectorsPerCluster == 0
                ? states.Length
                : floppy.Image.Bpb.TotalSectors / floppy.Image.Bpb.SectorsPerCluster;
            string fsLabel = FormatFatTypeLabel(floppy.Image.FatType);
            return DriveContentMap.ForFloppy(upper, clusterInfos, totalClusters, fsLabel);
        }
        for (int i = 0; i < _mscdex.Drives.Count; i++) {
            MscdexDriveEntry entry = _mscdex.Drives[i];
            if (entry.DriveLetter != upper) {
                continue;
            }
            ICdRomImage image = entry.Drive.Image;
            IReadOnlyList<CdTrack> rawTracks = image.Tracks;
            List<DriveCdTrackInfo> tracks = new(rawTracks.Count);
            for (int t = 0; t < rawTracks.Count; t++) {
                CdTrack track = rawTracks[t];
                int endLba = t + 1 < rawTracks.Count ? rawTracks[t + 1].StartLba : image.TotalSectors;
                int length = endLba > track.StartLba ? endLba - track.StartLba : 0;
                tracks.Add(new DriveCdTrackInfo(track.Number, (uint)track.StartLba, (uint)length, track.IsAudio));
            }
            return DriveContentMap.ForCdRom(upper, (uint)image.TotalSectors, tracks);
        }

        // Empty/unmounted drives are expected and should return a normal empty map.
        return DriveContentMap.ForFat(upper, System.Array.Empty<DriveClusterInfo>(), 0);
    }

    /// <summary>
    /// Gets the current DOS directory.
    /// </summary>
    public DosFileOperationResult GetCurrentDosDirectory(byte driveNumber, out string currentDir) {
        //0 = default drive
        if (driveNumber == 0 && Count > 0) {
            DosDriveBase virtualDrive = CurrentDrive;
            currentDir = virtualDrive.CurrentDosDirectory;
            return DosFileOperationResult.NoValue();
        } else {
            if (TryGetDriveAtIndex(driveNumber - 1, out DosDriveBase? virtualDrive)) {
                currentDir = virtualDrive.CurrentDosDirectory;
                return DosFileOperationResult.NoValue();
            }
        }
        currentDir = "";
        return DosFileOperationResult.LogError(DosErrorCode.InvalidDrive);
    }

    public DosDriveBase GetDrive(char driveLetter) => TryGetDrive(driveLetter, out DosDriveBase? drive)
            ? drive : throw new KeyNotFoundException($"Drive '{driveLetter}' is not mounted.");

    public T GetDrive<T>(char driveLetter) where T : DosDriveBase => TryGetDrive(driveLetter, out T? drive)
            ? drive : throw new KeyNotFoundException($"Drive '{driveLetter}' is not mounted.");

    public DosDriveBase GetDriveAtIndex(int driveIndex) => TryGetDriveAtIndex(driveIndex, out DosDriveBase? drive)
            ? drive : throw new KeyNotFoundException($"Drive at index {driveIndex} is not mounted.");

    public T GetDriveAtIndex<T>(int driveIndex) where T : DosDriveBase => TryGetDriveAtIndex(driveIndex, out T? drive)
            ? drive : throw new KeyNotFoundException($"Drive at index {driveIndex} is not mounted.");

    public IReadOnlyList<DosVirtualDriveStatus> GetDriveStatuses() {
        return new DosDriveStatusProvider(this, _mscdex).GetDriveStatuses();
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

    /// <inheritdoc />
    public IReadOnlyList<DriveFileEntry> GetFileList(char driveLetter) {
        char upper = char.ToUpperInvariant(driveLetter);

        // FAT image-backed floppy drive.
        if (TryGetFloppyDrive(upper, out FloppyDiskDrive? floppy) && floppy.Image != null) {
            return BuildFatEntries(floppy.Image, isRoot: true, firstCluster: 0);
        }

        // CD-ROM drives registered with MSCDEX.
        for (int i = 0; i < _mscdex.Drives.Count; i++) {
            MscdexDriveEntry entry = _mscdex.Drives[i];
            if (entry.DriveLetter != upper) {
                continue;
            }
            return BuildIsoEntries(entry.Drive.Image, entry.Drive.Image.PrimaryVolume.RootDirectoryLba, entry.Drive.Image.PrimaryVolume.RootDirectorySize);
        }

        // Folder-backed drive (HDD, folder floppy, folder CD).
        if (TryGetDrive<FolderDrive>(upper, out FolderDrive? vd) && !string.IsNullOrEmpty(vd.MountedHostDirectory)) {
            string hostRoot = vd.MountedHostDirectory.TrimEnd('/', '\\');
            return BuildHostEntries(hostRoot);
        }

        // Drive exists but has no mounted image or host folder (e.g., empty floppy slot).
        return System.Array.Empty<DriveFileEntry>();
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
    /// Converts the DOS path to a full host path of the parent directory.<br/>
    /// </summary>
    /// <param name="dosPath">The DOS path to convert.</param>
    /// <returns>A string containing the full path to the parent directory in the host file system, or <c>null</c> if nothing was found.</returns>
    public string? GetFullHostParentPathFromDosOrDefault(string dosPath) {
        string? parentPath = Path.GetDirectoryName(dosPath);
        if (string.IsNullOrWhiteSpace(parentPath)) {
            parentPath = GetFullCurrentDosPathOnDrive(CurrentDrive);
        }
        string? fullHostPath = GetFullHostPathFromDosOrDefault(parentPath);
        if (string.IsNullOrWhiteSpace(fullHostPath)) {
            return null;
        }
        return ConvertUtils.ToSlashFolderPath(fullHostPath);
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

    /// <inheritdoc/>
    public FloppyGeometryResult GetGeometry(byte driveNumber) {
        ImageBackedFloppyDrive imageDrive = ResolveImageBackedDrive(driveNumber);
        if (!imageDrive.IsPresent) {
            return FloppyGeometryResult.DriveNotReady;
        }

        if (TryGetImageGeometry(imageDrive.ImageData, out int totalCylinders, out int headsPerCylinder,
                out int sectorsPerTrack, out int bytesPerSector)) {
            FloppyGeometry geometry = new(totalCylinders, headsPerCylinder, sectorsPerTrack, bytesPerSector);
            return FloppyGeometryResult.Success(geometry);
        }

        return FloppyGeometryResult.DriveNotReady;
    }

    /// <summary>Returns <c>true</c> when <paramref name="driveLetter"/> currently refers to a SUBST drive.</summary>
    public bool IsSubstDrive(char driveLetter) =>
        _substDriveMap.ContainsKey(NormalizeDriveLetter(driveLetter));

    /// <summary>In-segment offset of the given drive's entry, used as BX in AH=1Bh/1Ch returns.</summary>
    public ushort MediaIdEntryOffset(byte driveIndex) => _mediaIdTable.EntryOffset(driveIndex);

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
    /// Mounts a host folder as a folder-backed floppy drive (A: or B:).
    /// </summary>
    public void MountFloppyFolder(char driveLetter, string hostFolderPath) {
        char upper = NormalizeDriveLetter(driveLetter);
        ReplaceDrive(upper, new FolderDrive {
            DriveLetter = upper,
            MountedHostDirectory = ConvertUtils.ToSlashFolderPath(hostFolderPath),
        });
        if (_loggerService.IsEnabled(LogLevel.Debug)) {
            _loggerService.LogDebug("DosDriveManager: mounted folder {Path} as {Drive}:", hostFolderPath, upper);
        }
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
        if (_loggerService.IsEnabled(LogLevel.Information)) {
            _loggerService.LogInformation("IMGMOUNT: Mounted image {Image} on drive {Drive}:", imagePath, upper);
        }
    }

    /// <inheritdoc/>
    public void MountFolderAsCdRom(char driveLetter, string hostPath) {
        if (string.IsNullOrWhiteSpace(hostPath) || !Directory.Exists(hostPath)) {
            throw new DirectoryNotFoundException($"CD-ROM mount folder was not found: {hostPath}");
        }

        string volumeLabel = Path.GetFileName(hostPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        VirtualIsoImage image = new VirtualIsoImage(hostPath, volumeLabel);
        char upper = char.ToUpperInvariant(driveLetter);
        CdRomDrive drive = new CdRomDrive(image, _channelCreator, _activityNotifier, upper);
        byte driveIndex = GetDriveIndexOrDefault(upper);
        MscdexDriveEntry entry = new MscdexDriveEntry(upper, driveIndex, drive);
        _mscdex.AddDrive(entry);
        RegisterCdRomDriveLetter(upper, hostPath, volumeLabel, drive);
        if (_loggerService.IsEnabled(LogLevel.Information)) {
            _loggerService.LogInformation("MOUNT: Drive {Drive}: is now backed by folder {Path}", upper, hostPath);
        }
    }

    /// <inheritdoc/>
    public void MountFolderAsFloppy(char driveLetter, string hostPath) {
        if (string.IsNullOrWhiteSpace(hostPath) || !Directory.Exists(hostPath)) {
            throw new DirectoryNotFoundException($"Floppy mount folder was not found: {hostPath}");
        }

        MountFloppyFolder(driveLetter, hostPath);
        if (_loggerService.IsEnabled(LogLevel.Information)) {
            _loggerService.LogInformation("MOUNT: Drive {Drive}: is now backed by folder {Path}", char.ToUpperInvariant(driveLetter), hostPath);
        }
    }

    /// <summary>
    /// Mounts a host folder as a regular (HDD-style) DOS drive.
    /// Adds the drive if it does not already exist, or replaces the existing entry.
    /// </summary>
    public void MountFolderDrive(char driveLetter, string hostFolderPath) {
        char upper = NormalizeDriveLetter(driveLetter);
        FolderDrive newDrive = new FolderDrive {
            DriveLetter = upper,
            MountedHostDirectory = ConvertUtils.ToSlashFolderPath(hostFolderPath),
        };
        ReplaceDrive(upper, newDrive);
        if (CurrentDrive.DriveLetter == upper) {
            CurrentDrive = newDrive;
        }
        if (_loggerService.IsEnabled(LogLevel.Information)) {
            _loggerService.LogInformation("MOUNT: Drive {Drive}: is now backed by folder {Path}", upper, hostFolderPath);
        }
    }

    /// <inheritdoc/>
    public void MountImageAsCdRom(char driveLetter, string imagePath) {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath)) {
            throw new FileNotFoundException("CD-ROM image was not found", imagePath);
        }

        ICdRomImage image = CdRomImageFactory.Open(imagePath);
        char upper = char.ToUpperInvariant(driveLetter);
        CdRomDrive drive = new CdRomDrive(image, _channelCreator, _activityNotifier, upper);
        byte driveIndex = GetDriveIndexOrDefault(upper);
        MscdexDriveEntry entry = new MscdexDriveEntry(upper, driveIndex, drive);
        _mscdex.AddDrive(entry);
        string volumeLabel = image.PrimaryVolume.VolumeIdentifier ?? string.Empty;
        RegisterCdRomDriveLetter(upper, string.Empty, volumeLabel, drive);
        if (_loggerService.IsEnabled(LogLevel.Information)) {
            _loggerService.LogInformation("IMGMOUNT: Mounted image {Image} on drive {Drive}:", imagePath, upper);
        }
    }

    /// <inheritdoc/>
    public void MountImageAsFloppy(char driveLetter, string imagePath) {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath)) {
            throw new FileNotFoundException("Floppy image was not found", imagePath);
        }

        byte[] imageData = File.ReadAllBytes(imagePath);
        MountFloppyImage(driveLetter, imageData, imagePath);
    }

    /// <summary>
    /// Mounts a memory-backed drive (typically Z: for AUTOEXEC.BAT).
    /// </summary>
    /// <param name="drive">The memory drive to mount.</param>
    public void MountMemoryDrive(MemoryDrive drive) => Mount(drive);

    /// <summary>
    /// Mounts a host folder as a SUBST drive.
    /// </summary>
    public void MountSubstDrive(char driveLetter, string hostFolderPath, string originalDosPath) {
        char upper = NormalizeDriveLetter(driveLetter);
        MountFolderDrive(upper, hostFolderPath);
        _substDriveMap[upper] = originalDosPath;
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

    /// <inheritdoc/>
    public FloppyTransferResult ReadFromImage(byte driveNumber, int imageByteOffset, byte[] destination, int destOffset, int byteCount) {
        ImageBackedFloppyDrive imageDrive = ResolveImageBackedDrive(driveNumber);
        if (!imageDrive.IsPresent) {
            return FloppyTransferResult.DriveNotReady;
        }
        if (imageByteOffset < 0 || imageByteOffset + byteCount > imageDrive.ImageData.Length) {
            return FloppyTransferResult.OutOfRange;
        }

        imageDrive.ImageData.AsSpan(imageByteOffset, byteCount).CopyTo(destination.AsSpan(destOffset));
        return FloppyTransferResult.Success(byteCount);
    }

    /// <summary>
    /// Registers a drive letter for a CD-ROM drive so that drive-change commands
    /// (e.g. <c>D:</c>) succeed after <c>IMGMOUNT</c> or <c>MOUNT -t cdrom</c>.
    /// </summary>
    public void RegisterCdRomDriveLetter(char driveLetter, string hostFolderPath, string volumeLabel) {
        RegisterCdRomDriveLetter(driveLetter, hostFolderPath, volumeLabel, null);
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
    /// Sets the current DOS folder.
    /// </summary>
    /// <param name="dosPath">The new DOS path to use as the current DOS folder.</param>
    /// <returns>A <see cref="DosFileOperationResult"/> that details the result of the operation.</returns>
    public DosFileOperationResult SetCurrentDir(string dosPath) {
        string? fullDosPath = GetFullDosPathIncludingRoot(dosPath);

        if (fullDosPath is null || !StartsWithDosDriveAndVolumeSeparator(fullDosPath)) {
            return DosFileOperationResult.LogError(DosErrorCode.PathNotFound);
        }

        if (TryGetDrive(fullDosPath[0], out DosDriveBase? drive) &&
            drive is IDosPathContent content && content.DirectoryExists(fullDosPath[3..])) {
            drive.CurrentDosDirectory = fullDosPath[3..];
            return DosFileOperationResult.NoValue();
        }

        string? hostPath = GetFullHostPathFromDosOrDefault(fullDosPath);
        if (!string.IsNullOrWhiteSpace(hostPath)) {
            return SetCurrentDirValue(fullDosPath[0], hostPath, fullDosPath);
        } else {
            return DosFileOperationResult.LogError(DosErrorCode.PathNotFound);
        }
    }

    /// <inheritdoc/>
    public void SwapDiscImages() {
        SwapFloppyDiscs();
        foreach (MscdexDriveEntry entry in _mscdex.Drives) {
            entry.Drive.SwapToNextDisc();
            if (_loggerService.IsEnabled(LogLevel.Information)) {
                _loggerService.LogInformation("MOUNT: Swapping drive {Drive}: to image {Image}", entry.DriveLetter, entry.Drive.Image.ImagePath);
            }
        }
    }

    /// <summary>
    /// Advances every floppy drive that has more than one image to the next image in its list.
    /// </summary>
    public void SwapFloppyDiscs() {
        for (int i = 0; i < MaxDriveCount; i++) {
            if (_driveMap[i] is FloppyDiskDrive floppy && floppy.HasImage) {
                floppy.SwapToNextImage();
                if (_loggerService.IsEnabled(LogLevel.Information)) {
                    _loggerService.LogInformation("MOUNT: Swapping drive {Drive}: to image {Image}", floppy.DriveLetter, floppy.ImagePath);
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
        if (_loggerService.IsEnabled(LogLevel.Information)) {
            _loggerService.LogInformation("MOUNT: Drive {Drive}: switched to image {Image}", upper, drive.ImagePath);
        }
    }

    /// <inheritdoc/>
    public void SwapToImageIndex(char driveLetter, int imageIndex) {
        char upper = char.ToUpperInvariant(driveLetter);
        if (TryGetFloppyDrive(upper, out FloppyDiskDrive? floppy)) {
            floppy.SwapToIndex(imageIndex);
            if (_loggerService.IsEnabled(LogLevel.Information)) {
                _loggerService.LogInformation("MOUNT: Drive {Drive}: switched to image {Image}", upper, floppy.ImagePath);
            }
            return;
        }
        foreach (MscdexDriveEntry entry in _mscdex.Drives) {
            if (char.ToUpperInvariant(entry.DriveLetter) == upper) {
                entry.Drive.SwapToIndex(imageIndex);
                if (_loggerService.IsEnabled(LogLevel.Information)) {
                    _loggerService.LogInformation("MOUNT: Drive {Drive}: switched to image {Image}", upper, entry.Drive.Image.ImagePath);
                }
                return;
            }
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
            driveIndex = GetDriveIndex(CurrentDrive.DriveLetter);
            if (driveIndex == -1) {
                // Current drive has a bad drive letter.
                return false;
            }
        }

        Debug.Assert(driveIndex is >= 0 and < DosDriveManager.MaxDriveCount);
        return true;
    }

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
    /// Tries to get a floppy drive by letter, returning <see langword="true"/> only when raw image data is mounted.
    /// </summary>
    /// <param name="driveLetter">The drive letter to look up.</param>
    /// <param name="drive">The floppy drive if found; null otherwise.</param>
    /// <returns>True if a raw image is mounted on the specified letter; false otherwise.</returns>
    public bool TryGetFloppyDrive(char driveLetter, [MaybeNullWhen(false)] out FloppyDiskDrive drive) {
        if (TryGetDrive(driveLetter, out FloppyDiskDrive? f) && f.HasImage) {
            drive = f;
            return true;
        }
        drive = null;
        return false;
    }

    /// <summary>
    /// Tries to get a mounted memory drive by letter.
    /// </summary>
    /// <param name="driveLetter">The drive letter (e.g., 'Z').</param>
    /// <param name="drive">The memory drive if found; null otherwise.</param>
    /// <returns>True if memory drive exists; false otherwise.</returns>
    public bool TryGetMemoryDrive(char driveLetter, [MaybeNullWhen(false)] out MemoryDrive drive) {
        return TryGetDrive(driveLetter, out drive);
    }

    public bool TryGetValue(char key, [MaybeNullWhen(false)] out DosDriveBase value) => TryGetDrive(key, out value);

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

    /// <summary>
    /// Removes a previously-SUBST'd drive. Returns <c>false</c> when the drive letter is not currently SUBST'd.
    /// </summary>
    public bool UnmountSubstDrive(char driveLetter) {
        char upper = NormalizeDriveLetter(driveLetter);
        if (!_substDriveMap.Remove(upper)) {
            return false;
        }
        int idx = GetDriveIndex(upper);
        if (idx >= 0 && _driveMap[idx] is FolderDrive drive) {
            RemoveDriveInternal(drive, idx);
            if (CurrentDrive.DriveLetter == upper && TryGetDrive('C', out FolderDrive? cDrive)) {
                CurrentDrive = cDrive;
            }
            if (_loggerService.IsEnabled(LogLevel.Information)) {
                _loggerService.LogInformation("SUBST: Drive {Drive}: removed (was {Path})", upper, drive.MountedHostDirectory);
            }
        }
        return true;
    }

    /// <inheritdoc/>
    public FloppyTransferResult WriteToImage(byte driveNumber, int imageByteOffset, byte[] source, int srcOffset, int byteCount) {
        ImageBackedFloppyDrive imageDrive = ResolveImageBackedDrive(driveNumber);
        if (!imageDrive.IsPresent) {
            return FloppyTransferResult.DriveNotReady;
        }
        if (imageByteOffset < 0 || imageByteOffset + byteCount > imageDrive.ImageData.Length) {
            return FloppyTransferResult.OutOfRange;
        }

        source.AsSpan(srcOffset, byteCount).CopyTo(imageDrive.ImageData.AsSpan(imageByteOffset));
        imageDrive.Drive.MarkDirty();
        return FloppyTransferResult.Success(byteCount);
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

    internal static string GetExeParentFolder(string? exe) {
        string fallbackValue = ConvertUtils.ToSlashFolderPath(Environment.CurrentDirectory);
        if (string.IsNullOrWhiteSpace(exe)) {
            return fallbackValue;
        }
        string? parent = Path.GetDirectoryName(exe);
        return string.IsNullOrWhiteSpace(parent) ? fallbackValue : ConvertUtils.ToSlashFolderPath(parent);
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
        if (isRelativePath && TryGetDriveAtIndex(driveIndex, out DosDriveBase? drive)) {
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

    internal bool HasDriveAtIndex(int zeroBasedIndex) => zeroBasedIndex is >= 0 and < MaxDriveCount &&
                                    _driveMap[zeroBasedIndex] is not null;

    internal void InitializeBootstrapZDrive() {
        MemoryDrive zDrive = new MemoryDrive {
            DriveLetter = 'Z',
            Label = "MEMORY",
            IsReadOnlyMedium = true,
        };
        MountMemoryDrive(zDrive);
    }

    internal bool TryGetContentEntry(string dosPath, out DosContentEntry? entry) {
        entry = null;
        string? fullDosPath = GetFullDosPathIncludingRoot(dosPath);
        if (fullDosPath is null || fullDosPath.Length < 3 ||
            !TryGetDrive(fullDosPath[0], out DosDriveBase? drive) ||
            drive is not IDosPathContent) {
            return false;
        }

        string relativePath = fullDosPath[3..];
        int separator = relativePath.LastIndexOf(DirectorySeparatorChar);
        string directoryPath = separator < 0 ? string.Empty : relativePath[..separator];
        string name = separator < 0 ? relativePath : relativePath[(separator + 1)..];
        if (!TryGetDirectoryEntries($"{drive.DosVolume}{DirectorySeparatorChar}{directoryPath}",
                out IReadOnlyList<DosContentEntry> entries)) {
            return true;
        }
        entry = entries.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
        return true;
    }

    internal bool TryGetDirectoryEntries(string dosPath, out IReadOnlyList<DosContentEntry> entries) {
        entries = Array.Empty<DosContentEntry>();
        string? fullDosPath = GetFullDosPathIncludingRoot(dosPath);
        if (fullDosPath is null || fullDosPath.Length < 3 ||
            !TryGetDrive(fullDosPath[0], out DosDriveBase? drive) ||
            drive is not IDosPathContent content) {
            return false;
        }

        entries = content.GetDirectoryEntries(fullDosPath[3..]);
        return true;
    }

    internal bool TryOpenRead(string dosPath, out string? fullDosPath, out Stream? stream) {
        fullDosPath = GetFullDosPathIncludingRoot(dosPath);
        stream = null;
        if (fullDosPath is null || fullDosPath.Length < 3 ||
            !TryGetDrive(fullDosPath[0], out DosDriveBase? drive) ||
            drive is not IDosPathContent content) {
            return false;
        }

        return content.TryOpenRead(fullDosPath[3..], out stream);
    }

    private static List<DriveFileEntry> BuildFatEntries(FatFileSystem fs, bool isRoot, uint firstCluster) {
        IReadOnlyList<FatDirectoryEntry> raw = isRoot
            ? fs.ListRootDirectory()
            : fs.ListSubDirectory(firstCluster);

        List<DriveFileEntry> dirs = new();
        List<DriveFileEntry> files = new();
        for (int i = 0; i < raw.Count; i++) {
            FatDirectoryEntry e = raw[i];
            if (e.IsDirectory) {
                IReadOnlyList<DriveFileEntry> children = BuildFatEntries(fs, isRoot: false, firstCluster: e.FirstCluster);
                dirs.Add(new DriveFileEntry(e.DosName, 0, FormatFatAttributes(e.Attributes), isDirectory: true, children));
            } else {
                files.Add(new DriveFileEntry(e.DosName, e.FileSize, FormatFatAttributes(e.Attributes), isDirectory: false, System.Array.Empty<DriveFileEntry>()));
            }
        }
        dirs.AddRange(files);
        return dirs;
    }

    private static List<DriveFileEntry> BuildHostEntries(string hostPath) {
        if (!Directory.Exists(hostPath)) {
            return new();
        }
        DirectoryInfo dir = new DirectoryInfo(hostPath);
        DirectoryInfo[] subdirs = dir.GetDirectories();
        FileInfo[] fileInfos = dir.GetFiles();

        List<DriveFileEntry> dirs = new(subdirs.Length);
        for (int i = 0; i < subdirs.Length; i++) {
            DirectoryInfo sub = subdirs[i];
            IReadOnlyList<DriveFileEntry> children = BuildHostEntries(sub.FullName);
            dirs.Add(new DriveFileEntry(sub.Name.ToUpperInvariant(), 0, "D", isDirectory: true, children));
        }

        List<DriveFileEntry> files = new(fileInfos.Length);
        for (int i = 0; i < fileInfos.Length; i++) {
            FileInfo fi = fileInfos[i];
            string attrs = FormatHostAttributes(fi.Attributes);
            files.Add(new DriveFileEntry(fi.Name.ToUpperInvariant(), fi.Length, attrs, isDirectory: false, System.Array.Empty<DriveFileEntry>()));
        }

        dirs.Sort((a, b) => string.Compare(a.Name, b.Name, System.StringComparison.OrdinalIgnoreCase));
        files.Sort((a, b) => string.Compare(a.Name, b.Name, System.StringComparison.OrdinalIgnoreCase));
        dirs.AddRange(files);
        return dirs;
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

    private static string FormatFatAttributes(byte attr) {
        List<string> parts = new();
        if ((attr & 0x01) != 0) {
            parts.Add("R");
        }
        if ((attr & 0x02) != 0) {
            parts.Add("H");
        }
        if ((attr & 0x04) != 0) {
            parts.Add("S");
        }
        if ((attr & 0x10) != 0) {
            parts.Add("D");
        }
        if ((attr & 0x20) != 0) {
            parts.Add("A");
        }
        return parts.Count == 0 ? "---" : string.Join("", parts);
    }

    private static string FormatFatTypeLabel(FatType fatType) {
        if (fatType == FatType.Fat12) {
            return "FAT12";
        }
        if (fatType == FatType.Fat16) {
            return "FAT16";
        }
        if (fatType == FatType.Fat32) {
            return "FAT32";
        }
        return string.Empty;
    }

    private static string FormatHostAttributes(System.IO.FileAttributes attr) {
        List<string> parts = new();
        if ((attr & System.IO.FileAttributes.ReadOnly) != 0) {
            parts.Add("R");
        }
        if ((attr & System.IO.FileAttributes.Hidden) != 0) {
            parts.Add("H");
        }
        if ((attr & System.IO.FileAttributes.System) != 0) {
            parts.Add("S");
        }
        if ((attr & System.IO.FileAttributes.Archive) != 0) {
            parts.Add("A");
        }
        return parts.Count == 0 ? "---" : string.Join("", parts);
    }

    private static string GetFullCurrentDosPathOnDrive(DosDriveBase virtualDrive) =>
            Path.Join($"{virtualDrive.DosVolume}{DirectorySeparatorChar}", virtualDrive.CurrentDosDirectory);

    private static bool GetGeometryFromBpb(byte[] imageData, out int totalCylinders, out int headsPerCylinder,
            out int sectorsPerTrack, out int bytesPerSector) {
        totalCylinders = 0;
        headsPerCylinder = 0;
        sectorsPerTrack = 0;
        bytesPerSector = 0;

        if (imageData.Length < 62) {
            return false;
        }

        ushort bytesPerSectorCandidate = BitConverter.ToUInt16(imageData, 11);
        if (bytesPerSectorCandidate == 0) {
            return false;
        }

        FatBiosParameterBlock bpb = ParseBpb(imageData);

        bytesPerSector = bpb.BytesPerSector;
        sectorsPerTrack = bpb.SectorsPerTrack;
        headsPerCylinder = bpb.NumberOfHeads;
        int totalSectors = bpb.TotalSectors;
        if (bytesPerSector <= 0 || sectorsPerTrack <= 0 || headsPerCylinder <= 0 || totalSectors <= 0) {
            return false;
        }

        int tracksPerCylinder = sectorsPerTrack * headsPerCylinder;
        if (tracksPerCylinder <= 0) {
            return false;
        }

        totalCylinders = totalSectors / tracksPerCylinder;
        return totalCylinders > 0;
    }

    private static bool GetGeometryFromImageSize(byte[] imageData, out int totalCylinders,
            out int headsPerCylinder, out int sectorsPerTrack, out int bytesPerSector) {
        totalCylinders = 0;
        headsPerCylinder = 0;
        sectorsPerTrack = 0;
        bytesPerSector = 0;

        const int BytesPerKilobyte = 1024;
        const int RawFloppyBytesPerSector = 512;
        if (imageData.Length % BytesPerKilobyte != 0) {
            return false;
        }

        int sizeInKilobytes = imageData.Length / BytesPerKilobyte;
        bytesPerSector = RawFloppyBytesPerSector;
        switch (sizeInKilobytes) {
            case 160:
                sectorsPerTrack = 8;
                headsPerCylinder = 1;
                totalCylinders = 40;
                return true;

            case 180:
                sectorsPerTrack = 9;
                headsPerCylinder = 1;
                totalCylinders = 40;
                return true;

            case 200:
                sectorsPerTrack = 10;
                headsPerCylinder = 1;
                totalCylinders = 40;
                return true;

            case 320:
                sectorsPerTrack = 8;
                headsPerCylinder = 2;
                totalCylinders = 40;
                return true;

            case 360:
                sectorsPerTrack = 9;
                headsPerCylinder = 2;
                totalCylinders = 40;
                return true;

            case 400:
                sectorsPerTrack = 10;
                headsPerCylinder = 2;
                totalCylinders = 40;
                return true;

            case 720:
                sectorsPerTrack = 9;
                headsPerCylinder = 2;
                totalCylinders = 80;
                return true;

            case 1200:
                sectorsPerTrack = 15;
                headsPerCylinder = 2;
                totalCylinders = 80;
                return true;

            case 1440:
                sectorsPerTrack = 18;
                headsPerCylinder = 2;
                totalCylinders = 80;
                return true;

            case 1520:
                sectorsPerTrack = 19;
                headsPerCylinder = 2;
                totalCylinders = 80;
                return true;

            case 1680:
                sectorsPerTrack = 21;
                headsPerCylinder = 2;
                totalCylinders = 80;
                return true;

            case 1720:
                sectorsPerTrack = 21;
                headsPerCylinder = 2;
                totalCylinders = 82;
                return true;

            case 1840:
                sectorsPerTrack = 23;
                headsPerCylinder = 2;
                totalCylinders = 80;
                return true;

            case 2880:
                sectorsPerTrack = 36;
                headsPerCylinder = 2;
                totalCylinders = 80;
                return true;

            default:
                bytesPerSector = 0;
                return false;
        }
    }

    private static IEnumerable<string> GetTopLevelDirsAndFiles(string hostPath, string searchPattern = "*") {
        return Directory
            .GetDirectories(hostPath, searchPattern)
            .Concat(Directory.GetFiles(hostPath, searchPattern));
    }

    private static bool IsWithinMountPoint(string hostFullPath, FolderDrive? virtualDrive) =>
            virtualDrive is not null && hostFullPath.StartsWith(virtualDrive.MountedHostDirectory);

    private static FatBiosParameterBlock ParseBpb(byte[] imageData) {
        return FatBiosParameterBlock.Parse(imageData.AsSpan(0, Math.Min(512, imageData.Length)));
    }

    private static bool PatternNameIsEmpty(ReadOnlySpan<char> pattern) {
        int dotPos = pattern.LastIndexOf('.');
        return dotPos == 0; // begins with a dot, so name part length is zero
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

    private static void ToUpperCopy(ReadOnlySpan<char> src, Span<char> dst) {
        src.ToUpperInvariant(dst);
    }

    private static bool TryGetImageGeometry(byte[] imageData, out int totalCylinders, out int headsPerCylinder,
            out int sectorsPerTrack, out int bytesPerSector) {
        if (GetGeometryFromBpb(imageData, out totalCylinders, out headsPerCylinder, out sectorsPerTrack,
                out bytesPerSector)) {
            return true;
        }

        return GetGeometryFromImageSize(imageData, out totalCylinders, out headsPerCylinder, out sectorsPerTrack,
            out bytesPerSector);
    }

    private static bool WildcardMatchesHiddenFile(ReadOnlySpan<char> fileName, ReadOnlySpan<char> wildcard) {
        if (fileName.IsEmpty) {
            return false;
        }

        return fileName.Length >= 5 && fileName[0] == '.' &&
               !fileName.Equals(".", StringComparison.Ordinal) &&
               !fileName.Equals("..", StringComparison.Ordinal);
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

    private List<DriveFileEntry> BuildIsoEntries(ICdRomImage image, int dirLba, int dirSize) {
        int sectorSize = image.PrimaryVolume.LogicalBlockSize;
        if (sectorSize <= 0) {
            sectorSize = 2048;
        }
        int sectorsNeeded = (dirSize + sectorSize - 1) / sectorSize;
        byte[] buf = new byte[sectorSize];
        List<IsoDirectoryRecord> records = new();
        for (int s = 0; s < sectorsNeeded; s++) {
            image.Read(dirLba + s, buf, CdSectorMode.CookedData2048);
            int offset = 0;
            while (offset < buf.Length) {
                if (buf[offset] == 0) {
                    break;
                }
                ReadOnlySpan<byte> span = buf.AsSpan(offset);
                IsoDirectoryRecord? rec = IsoDirectoryRecord.ParseNullable(span);
                if (rec == null) {
                    break;
                }
                records.Add(rec);
                offset += buf[offset];
            }
        }

        List<DriveFileEntry> dirs = new();
        List<DriveFileEntry> files = new();
        for (int i = 0; i < records.Count; i++) {
            IsoDirectoryRecord rec = records[i];
            if (rec.Name is "\x00" or "\x01") {
                continue;
            }
            if (rec.IsDirectory) {
                IReadOnlyList<DriveFileEntry> children = BuildIsoEntries(image, rec.ExtentLba, rec.DataLength);
                dirs.Add(new DriveFileEntry(rec.Name, 0, "D", isDirectory: true, children));
            } else {
                files.Add(new DriveFileEntry(rec.Name, rec.DataLength, "---", isDirectory: false, System.Array.Empty<DriveFileEntry>()));
            }
        }
        dirs.AddRange(files);
        return dirs;
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
        if (!TryGetDrive(dosPath[0], out FolderDrive? drive)) {
            return (null, dosPath[3..]);
        }

        return (drive.MountedHostDirectory, dosPath[3..]);
    }

    private byte GetDriveIndexOrDefault(char driveLetter) {
        if (TryGetLetterIndex(driveLetter, out int index)) {
            return (byte)index;
        }

        return DefaultCdDriveIndex;
    }

    /// <summary>Writes the FAT media descriptor byte for every drive into the media ID table.</summary>
    private void InitializeMediaDescriptors() {
        for (byte driveIndex = 0; driveIndex < MaxDriveCount; driveIndex++) {
            _mediaIdTable[driveIndex] = MediaDescriptor(driveIndex);
        }
    }

    private bool IsPathRooted(string path) =>
        path.StartsWith(DirectorySeparatorChar) ||
        path.StartsWith(AltDirectorySeparatorChar) ||
        (path.Length >= 3 &&
        StartsWithDosDriveAndVolumeSeparator(path) &&
        path[2] == DirectorySeparatorChar);

    private byte MediaDescriptor(byte driveIndex) {
        if (driveIndex <= 1) {
            return FloppyMediaDescriptor;
        }
        return FixedDiskMediaDescriptor;
    }

    private void RegisterCdRomDriveLetter(char driveLetter, string hostFolderPath, string volumeLabel,
        CdRomDrive? contentDrive) {
        char upper = NormalizeDriveLetter(driveLetter);
        string mountPath = string.IsNullOrEmpty(hostFolderPath)
            ? string.Empty
            : ConvertUtils.ToSlashFolderPath(hostFolderPath);
        CdRomDosDrive newDrive = new() {
            DriveLetter = upper,
            Label = volumeLabel,
            IsReadOnlyMedium = true,
            ImageContent = contentDrive is null ? null : new IsoDosPathContent(contentDrive),
        };
        ReplaceDrive(upper, newDrive);
        if (CurrentDrive.DriveLetter == upper) {
            CurrentDrive = newDrive;
        }
    }

    private void RemoveDriveInternal(DosDriveBase drive, int driveIndex) {
        Debug.Assert(driveIndex is >= 0 and < MaxDriveCount);
        try {
            if (drive is CdRomDosDrive) {
                _mscdex.RemoveDrive(drive.DriveLetter);
            }
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

    private ImageBackedFloppyDrive ResolveImageBackedDrive(byte driveNumber) {
        if (!TryGetDriveLetterFromIndex(driveNumber, out char driveLetter) || !TryGetFloppyDrive(driveLetter, out FloppyDiskDrive? floppy)) {
            return ImageBackedFloppyDrive.None;
        }

        byte[]? imageData = floppy.GetCurrentImageData();
        if (imageData == null) {
            return ImageBackedFloppyDrive.None;
        }

        return ImageBackedFloppyDrive.From(floppy, imageData);
    }

    private DosFileOperationResult SetCurrentDirValue(char driveLetter, string? hostFullPath, string fullDosPath) {
        if (string.IsNullOrWhiteSpace(hostFullPath) ||
            !IsWithinMountPoint(hostFullPath, TryGetDrive(driveLetter, out FolderDrive? vDrive) ? vDrive : null) ||
            Encoding.ASCII.GetByteCount(fullDosPath) > MaxPathLength) {
            return DosFileOperationResult.LogError(DosErrorCode.PathNotFound);
        }

        this[driveLetter].CurrentDosDirectory = fullDosPath[3..];
        return DosFileOperationResult.NoValue();
    }

    private bool StartsWithDosDriveAndVolumeSeparator(string dosPath) =>
        dosPath.Length >= 2 &&
        DosDriveManager.GetDriveIndex(dosPath[0]) != -1 &&
        dosPath[1] == VolumeSeparatorChar;

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

    public struct Enumerator : IEnumerator<KeyValuePair<char, DosDriveBase>>, IDictionaryEnumerator {
        internal const int ReturnTypeDictionaryEntry = 1;
        internal const int ReturnTypeKeyValuePair = 2;
        private readonly DosDriveManager? _dictionary;
        private readonly int _getEnumeratorReturnType;
        private readonly uint _version; // To make sure MoveNext() fails if dictionary changes while enumerating.
        private KeyValuePair<char, DosDriveBase> _current;
        private int _index; // One-based drive letter index or zero if before start or MaxDriveCount if at end.

        // What should Enumerator.Current return?
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

        public readonly void Dispose() {
        }

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

        IEnumerator IEnumerable.GetEnumerator() {
            return GetEnumerator();
        }

        bool ICollection<DosDriveBase>.Remove(DosDriveBase item) {
            throw new NotSupportedException("Mutating a value collection derived from a dictionary is not allowed.");
        }

        public struct Enumerator : IEnumerator<DosDriveBase> {
            private readonly DosDriveManager? _dictionary;
            private readonly uint _version; // To make sure MoveNext() fails if dictionary changes while enumerating.
            private DosDriveBase? _current;
            private int _index; // One-based drive letter index or zero if before start or MaxDriveCount+1 if at end.

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

            public readonly void Dispose() {
            }

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

        IEnumerator IEnumerable.GetEnumerator() {
            return GetEnumerator();
        }

        bool ICollection<char>.Remove(char item) {
            throw new NotSupportedException("Mutating a key collection derived from a dictionary is not allowed.");
        }

        public struct Enumerator : IEnumerator<char> {
            private readonly DosDriveManager? _dictionary;
            private readonly uint _version; // To make sure MoveNext() fails if dictionary changes while enumerating.
            private char _current;
            private int _index; // One-based drive letter index or zero if before start or MaxDriveCount if at end.

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

            public readonly void Dispose() {
            }

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

    private readonly record struct ImageBackedFloppyDrive(bool IsPresent, FloppyDiskDrive Drive, byte[] ImageData) {
        public static ImageBackedFloppyDrive None { get; } = new(false, new FloppyDiskDrive(), Array.Empty<byte>());

        public static ImageBackedFloppyDrive From(FloppyDiskDrive drive, byte[] imageData) {
            return new ImageBackedFloppyDrive(true, drive, imageData);
        }
    }
}