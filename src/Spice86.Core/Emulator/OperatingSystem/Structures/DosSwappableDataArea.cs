namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure;


/// <summary>
/// Represents the DOS swappable data area.
/// </summary>
public class DosSwappableDataArea : MemoryBasedDataStructure {
    public const ushort BaseSegment = 0xB2;

    public DosSwappableDataArea(IByteReaderWriter byteReaderWriter, uint baseAddress) : base(byteReaderWriter, baseAddress) {
        InDosFlag = 0;
    }

    /// <summary>
    /// The offset in bytes where the InDOS flag is located.
    /// </summary>
    public const int InDosFlagOffset = 0x01;

    /// <summary>
    /// Gets or sets the InDOS flag.
    /// </summary>
    public byte InDosFlag {
        get => UInt8[InDosFlagOffset];
        set => UInt8[InDosFlagOffset] = value;
    }
}
