namespace Spice86.Core.Emulator.OperatingSystem;

using Spice86.Core.Emulator.Devices.Storage;
using Spice86.Core.Emulator.OperatingSystem.FileSystem;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

/// <summary>
/// The class responsible for centralizing all the mounted DOS drives.
/// Implements <see cref="IFloppyDriveAccess"/> so the BIOS INT 13h handler can perform
/// low-level sector reads/writes without depending on any DOS-layer types.
/// </summary>
public class DosDriveManager : IDictionary<char, VirtualDrive>, IFloppyDriveAccess {
    private readonly SortedDictionary<char, VirtualDrive> _driveMap = new();
    private readonly Dictionary<char, MemoryDrive> _memoryDriveMap = new();
    private readonly Dictionary<char, FloppyDiskDrive> _floppyDriveMap = new();
    private readonly ILoggerService _loggerService;
    private readonly DosMediaIdTable _mediaIdTable;

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
    /// Mounts a host folder as a regular (HDD-style) DOS drive.
    /// Adds the drive if it does not already exist, or updates the existing entry.
    /// If the mounted drive is the currently active drive, <see cref="CurrentDrive"/> is also updated.
    /// </summary>
    /// <param name="driveLetter">The target drive letter (must not be 'A', 'B', or 'Z').</param>
    /// <param name="hostFolderPath">The absolute path to the host folder to mount.</param>
    public void MountFolderDrive(char driveLetter, string hostFolderPath) {
        char upper = char.ToUpperInvariant(driveLetter);
        VirtualDrive newDrive = new VirtualDrive {
            DriveLetter = upper,
            MountedHostDirectory = ConvertUtils.ToSlashFolderPath(hostFolderPath),
            CurrentDosDirectory = "",
        };
        _driveMap[upper] = newDrive;
        if (CurrentDrive.DriveLetter == upper) {
            CurrentDrive = newDrive;
        }
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _loggerService.Information("MOUNT: Drive {Drive}: is now backed by folder {Path}", upper, hostFolderPath);
        }
    }

    /// <summary>
    /// Gets a read-only view of all mounted memory drives, keyed by drive letter.
    /// </summary>
    public IReadOnlyDictionary<char, MemoryDrive> MemoryDrives => _memoryDriveMap;

    /// <summary>
    /// Tries to get a mounted memory drive by letter.
    /// </summary>
    /// <param name="driveLetter">The drive letter (e.g., 'Z').</param>
    /// <param name="drive">The memory drive if found; null otherwise.</param>
    /// <returns>True if memory drive exists; false otherwise.</returns>
    public bool TryGetMemoryDrive(char driveLetter, [MaybeNullWhen(false)] out MemoryDrive drive) {
        return _memoryDriveMap.TryGetValue(driveLetter, out drive);
    }

    /// <summary>
    /// Gets a read-only view of all floppy drives with an image mounted, keyed by drive letter.
    /// </summary>
    public IReadOnlyDictionary<char, FloppyDiskDrive> FloppyDrives => _floppyDriveMap;

    /// <summary>
    /// Mounts a floppy disk image (raw FAT12 bytes) to the specified drive letter (A: or B:).
    /// </summary>
    /// <param name="driveLetter">The target drive letter ('A' or 'B').</param>
    /// <param name="imageData">The raw bytes of the floppy disk image.</param>
    /// <param name="imagePath">The host file-system path of the image (used for display).</param>
    public void MountFloppyImage(char driveLetter, byte[] imageData, string imagePath) {
        FloppyDiskDrive floppy = new() { DriveLetter = driveLetter };
        floppy.MountImage(imageData, imagePath);
        _floppyDriveMap[char.ToUpperInvariant(driveLetter)] = floppy;
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _loggerService.Information("IMGMOUNT: Mounted image {Image} on drive {Drive}:", imagePath, char.ToUpperInvariant(driveLetter));
        }
    }

    /// <summary>
    /// Adds an additional floppy disk image to an already-mounted floppy drive,
    /// making it available for Ctrl-F4 disc switching.
    /// If no floppy drive is currently mounted on the letter, a new drive is created with this as the first image.
    /// </summary>
    /// <param name="driveLetter">The target drive letter ('A' or 'B').</param>
    /// <param name="imageData">The raw bytes of the floppy disk image.</param>
    /// <param name="imagePath">The host file-system path of the image (used for display).</param>
    public void AddFloppyImage(char driveLetter, byte[] imageData, string imagePath) {
        char upper = char.ToUpperInvariant(driveLetter);
        if (!_floppyDriveMap.TryGetValue(upper, out FloppyDiskDrive? floppy)) {
            floppy = new FloppyDiskDrive { DriveLetter = upper };
            floppy.MountImage(imageData, imagePath);
            _floppyDriveMap[upper] = floppy;
        } else {
            floppy.AddImage(imageData, imagePath);
        }
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _loggerService.Information("IMGMOUNT: Added image {Image} to drive {Drive}: ({Count} total)", imagePath, upper, _floppyDriveMap[upper].ImageCount);
        }
    }

    /// <summary>
    /// Advances every floppy drive that has more than one image to the next image in its list.
    /// </summary>
    public void SwapFloppyDiscs() {
        foreach (FloppyDiskDrive floppy in _floppyDriveMap.Values) {
            floppy.SwapToNextImage();
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
                _loggerService.Information("MOUNT: Swapping drive {Drive}: to image {Image}", floppy.DriveLetter, floppy.ImagePath);
            }
        }
    }

    /// <summary>Switches the floppy drive at <paramref name="letter"/> to the image at <paramref name="index"/>.</summary>
    /// <param name="letter">The floppy drive letter.</param>
    /// <param name="index">Zero-based index of the target image.</param>
    public void SwapFloppyToIndex(char letter, int index) {
        char upper = char.ToUpperInvariant(letter);
        if (!_floppyDriveMap.TryGetValue(upper, out FloppyDiskDrive? drive)) {
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
    /// <param name="driveLetter">The target drive letter ('A' or 'B').</param>
    /// <param name="hostFolderPath">The absolute path to the host folder to use as the floppy root.</param>
    /// <remarks>
    /// If an image-backed floppy drive was previously mounted on this letter it is unmounted before the folder
    /// is registered, so the drive reverts to host-filesystem path resolution.
    /// </remarks>
    public void MountFloppyFolder(char driveLetter, string hostFolderPath) {
        char upper = char.ToUpperInvariant(driveLetter);
        if (_floppyDriveMap.ContainsKey(upper)) {
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Debug)) {
                _loggerService.Debug("DosDriveManager: unmounting floppy image from {Drive}: before mounting folder", upper);
            }
            _floppyDriveMap.Remove(upper);
        }
        // Update the VirtualDrive entry so DosPathResolver can resolve paths normally.
        _driveMap[upper] = new VirtualDrive {
            DriveLetter = upper,
            MountedHostDirectory = ConvertUtils.ToSlashFolderPath(hostFolderPath),
            CurrentDosDirectory = "",
        };
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Debug)) {
            _loggerService.Debug("DosDriveManager: mounted folder {Path} as {Drive}:", hostFolderPath, upper);
        }
    }

    /// <summary>
    /// Tries to get a floppy drive by letter, returning <see langword="true"/> only when an image is mounted.
    /// </summary>
    /// <param name="driveLetter">The drive letter to look up.</param>
    /// <param name="drive">The floppy drive if found; null otherwise.</param>
    /// <returns>True if a floppy image is mounted on the specified letter; false otherwise.</returns>
    public bool TryGetFloppyDrive(char driveLetter, [MaybeNullWhen(false)] out FloppyDiskDrive drive) {
        return _floppyDriveMap.TryGetValue(char.ToUpperInvariant(driveLetter), out drive);
    }

    /// <summary>
    /// Registers a drive letter in the drive map for a CD-ROM drive so that drive-change
    /// commands (e.g. <c>D:</c> in a batch file) succeed after an <c>IMGMOUNT</c> or
    /// <c>MOUNT -t cdrom</c> operation.
    /// </summary>
    /// <param name="driveLetter">The drive letter to register (case-insensitive).</param>
    /// <param name="hostFolderPath">
    /// The host directory backing this drive.  Pass an empty string for image-backed
    /// CD-ROM drives (where file access goes through MSCDEX, not the host file system).
    /// Pass the host folder path for folder-backed CD-ROM drives so that <c>DIR D:</c>
    /// can fall through to the normal DOS path resolver.
    /// </param>
    public void RegisterCdRomDriveLetter(char driveLetter, string hostFolderPath) {
        char upper = char.ToUpperInvariant(driveLetter);
        string mountPath = string.IsNullOrEmpty(hostFolderPath)
            ? string.Empty
            : ConvertUtils.ToSlashFolderPath(hostFolderPath);
        _driveMap[upper] = new VirtualDrive {
            DriveLetter = upper,
            MountedHostDirectory = mountPath,
            CurrentDosDirectory = string.Empty,
        };
    }

    /// <inheritdoc/>
    public bool TryGetGeometry(byte driveNumber, out int totalCylinders, out int headsPerCylinder, out int sectorsPerTrack, out int bytesPerSector) {
        totalCylinders = 0;
        headsPerCylinder = 0;
        sectorsPerTrack = 0;
        bytesPerSector = 0;

        if (!TryResolveFloppyImage(driveNumber, out FloppyDiskDrive? _, out byte[]? imageData)) {
            return false;
        }

        FileSystem.BiosParameterBlock bpb = ParseBpb(imageData!);
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
    public bool TryRead(byte driveNumber, int imageByteOffset, byte[] destination, int destOffset, int byteCount) {
        if (!TryResolveFloppyImage(driveNumber, out FloppyDiskDrive? _, out byte[]? imageData)) {
            return false;
        }
        if (imageByteOffset < 0 || imageByteOffset + byteCount > imageData!.Length) {
            return false;
        }
        imageData.AsSpan(imageByteOffset, byteCount).CopyTo(destination.AsSpan(destOffset));
        return true;
    }

    /// <inheritdoc/>
    public bool TryWrite(byte driveNumber, int imageByteOffset, byte[] source, int srcOffset, int byteCount) {
        if (!TryResolveFloppyImage(driveNumber, out FloppyDiskDrive? floppy, out byte[]? imageData)) {
            return false;
        }
        if (imageByteOffset < 0 || imageByteOffset + byteCount > imageData!.Length) {
            return false;
        }
        source.AsSpan(srcOffset, byteCount).CopyTo(imageData.AsSpan(imageByteOffset));
        if (floppy is FloppyDiskDrive dirtyFloppy) {
            dirtyFloppy.MarkDirty();
        }
        return true;
    }

    private bool TryResolveFloppyImage(byte driveNumber, out FloppyDiskDrive? floppy, out byte[]? imageData) {
        floppy = null;
        imageData = null;
        char driveLetter = driveNumber == 0 ? 'A' : 'B';
        if (!TryGetFloppyDrive(driveLetter, out floppy)) {
            return false;
        }
        if (floppy.Image == null) {
            return false;
        }
        imageData = floppy.GetCurrentImageData();
        return imageData != null;
    }

    private static FileSystem.BiosParameterBlock ParseBpb(byte[] imageData) {
        return FileSystem.BiosParameterBlock.Parse(imageData.AsSpan(0, Math.Min(512, imageData.Length)));
    }
}
