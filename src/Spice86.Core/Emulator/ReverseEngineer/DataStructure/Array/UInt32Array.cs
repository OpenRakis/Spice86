namespace Spice86.Core.Emulator.ReverseEngineer.DataStructure.Array;

using Spice86.Core.Emulator.Memory.ReaderWriter;

/// <summary>
/// Represents an array of 32-bit unsigned integers stored in memory.
/// </summary>
public class Uint32Array : MemoryBasedArray<uint> {
    /// <summary>
    /// Initializes a new instance of the <see cref="Uint32Array"/> class with the specified memory, base address, and length.
    /// </summary>
    /// <param name="byteReaderWriter">Where data is read and written.</param>
    /// <param name="baseAddress">The base address of the array.</param>
    /// <param name="length">The length of the array.</param>
    public Uint32Array(IByteReaderWriter byteReaderWriter, uint baseAddress, int length) : base(byteReaderWriter, baseAddress, length) {
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
    public override uint this[int i] {
        get {
            uint offset = IndexToOffset(i);
            return UInt32[offset];
        }
        set {
            uint offset = IndexToOffset(i);
            UInt32[offset] = value;
        }
    }
}