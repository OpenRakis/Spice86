namespace Spice86.Core.Emulator.ReverseEngineer.DataStructure;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Represents a memory base structure with a base address.
/// </summary>
public class MemoryBasedDataStructure : AbstractMemoryBasedDataStructure {
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="byteReaderWriter">Where data is read and written.</param>
    /// <param name="baseAddress">The base address of the data structure.</param>
    public MemoryBasedDataStructure(IByteReaderWriter byteReaderWriter, SegmentedAddress baseAddress) : base(byteReaderWriter) {
        BaseAddress = baseAddress;
    }

    /// <summary>
    /// The base address of the data structure.
    /// </summary>
    public override SegmentedAddress BaseAddress { get; }
}