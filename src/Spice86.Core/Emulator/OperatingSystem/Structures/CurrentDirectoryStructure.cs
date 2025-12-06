namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure;

/// <summary>
/// Represents a DOS Current Directory Structure (CDS) entry.
/// Each CDS entry contains information about a drive's current directory.
/// </summary>
/// <remarks>
/// The CDS structure in DOS contains the current directory path for each drive.
/// This is a simplified read-only implementation for compatibility.
/// </remarks>
public class CurrentDirectoryStructure : MemoryBasedDataStructure {
    /// <summary>
    /// Size of a single CDS entry in bytes.
    /// </summary>
    public const int CdsEntrySize = 0x58; // 88 bytes per entry in DOS 5.0+

    /// <summary>
    /// Initializes a new instance of the <see cref="CurrentDirectoryStructure"/> class.
    /// The CDS is initialized with "C:\" as the default current directory path.
    /// </summary>
    /// <param name="byteReaderWriter">The memory reader/writer interface.</param>
    /// <param name="baseAddress">The base address of the CDS structure in memory.</param>
    public CurrentDirectoryStructure(IByteReaderWriter byteReaderWriter, uint baseAddress)
        : base(byteReaderWriter, baseAddress) {
        // Initialize with "C:\" - matches DOSBox behavior
        // 0x005c3a43 in little-endian = 0x43 ('C'), 0x3A (':'), 0x5C ('\'), 0x00 (null terminator)
        CurrentPath = 0x005c3a43;
    }

    /// <summary>
    /// Gets or sets the current directory path as a 4-byte value.
    /// This represents "C:\" in the default configuration.
    /// </summary>
    public uint CurrentPath {
        get => UInt32[0x00];
        set => UInt32[0x00] = value;
    }
}