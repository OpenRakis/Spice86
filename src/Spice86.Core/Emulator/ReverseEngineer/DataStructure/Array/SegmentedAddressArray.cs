namespace Spice86.Core.Emulator.ReverseEngineer.DataStructure.Array;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Represents an array of SegmentedAddress stored in memory.
/// </summary>
public class SegmentedAddressArray : MemoryBasedArray<SegmentedAddress> {
    /// <summary>
    /// Initializes a new instance of the <see cref="Uint32Array"/> class with the specified memory, base address, and length.
    /// </summary>
    /// <param name="byteReaderWriter">Where data is read and written.</param>
    /// <param name="baseAddress">The base address of the array.</param>
    /// <param name="length">The length of the array.</param>
    public SegmentedAddressArray(IByteReaderWriter byteReaderWriter, uint baseAddress, int length) : base(byteReaderWriter, baseAddress, length) {
    }

    /// <summary>
    /// Gets the size of each value in the array, in bytes.
    /// </summary>
    public override int ValueSize => 4;

    /// <summary>
    /// Gets or sets the value at the specified index in the array.
    /// </summary>
    /// <param name="i">The zero-based index of the value to get or set.</param>
    /// <returns>The value at the specified index.</returns>
    public override SegmentedAddress this[int i] {
        get {
            uint offset = IndexToOffset(i);
            return SegmentedAddress[offset];
        }
        set {
            uint offset = IndexToOffset(i);
            SegmentedAddress[offset] = value;
        }
    }
}