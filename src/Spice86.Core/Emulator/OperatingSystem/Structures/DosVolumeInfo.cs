namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure;
using Spice86.Shared.Emulator.Memory;

public class DosVolumeInfo : MemoryBasedDataStructure {
    private const int VolumeLabelLength = 11;
    private const int FileSystemTypeLength = 8;

    /// <summary>
    /// Initializes a new instance of the <see cref="DosVolumeInfo"/> class.
    /// </summary>
    /// <param name="byteReaderWriter">The memory bus.</param>
    /// <param name="baseAddress">The base address of the structure in memory.</param>
    public DosVolumeInfo(IByteReaderWriter byteReaderWriter, uint baseAddress) : base(byteReaderWriter, baseAddress) {
    }

    /// <summary>
    /// Gets or sets the information level for the call. Should be 0.
    /// </summary>
    public ushort InfoLevel {
        get => UInt16[0x00];
        set => UInt16[0x00] = value;
    }

    /// <summary>
    /// Gets or sets the 32-bit disk serial number.
    /// </summary>
    public uint SerialNumber {
        get => UInt32[0x02];
        set => UInt32[0x02] = value;
    }

    /// <summary>
    /// Gets or sets the 11-byte volume label.
    /// </summary>
    public string VolumeLabel {
        get => GetZeroTerminatedString(0x06, VolumeLabelLength);
        set => SetZeroTerminatedString(0x06, value.PadRight(VolumeLabelLength), VolumeLabelLength);
    }

    /// <summary>
    /// Gets or sets the 8-byte filesystem type (e.g., "FAT16   ").
    /// </summary>
    public string FileSystemType {
        get => GetZeroTerminatedString(0x11, FileSystemTypeLength);
        set => SetZeroTerminatedString(0x11, value.PadRight(FileSystemTypeLength), FileSystemTypeLength);
    }
}