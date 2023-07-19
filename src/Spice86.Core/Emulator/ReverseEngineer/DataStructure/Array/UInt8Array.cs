namespace Spice86.Core.Emulator.ReverseEngineer.DataStructure.Array;

using Spice86.Core.Emulator.Memory.ReaderWriter;

/// <summary>
/// Represents an array of unsigned 8-bit integers stored in memory.
/// </summary>
public class UInt8Array : MemoryBasedArray<byte> {
    /// <summary>
    /// Initializes a new instance of the <see cref="UInt8Array"/> class.
    /// </summary>
    /// <param name="byteReaderWriter">Where data is read and written.</param>
    /// <param name="baseAddress">The base address of the array in memory.</param>
    /// <param name="length">The length of the array.</param>
    public UInt8Array(IByteReaderWriter byteReaderWriter, uint baseAddress, int length) : base(byteReaderWriter, baseAddress, length) {
    }

    /// <summary>
    /// Gets the size of each element in the array.
    /// </summary>
    public override int ValueSize => 1;

    /// <summary>
    /// Gets or sets the value at the specified index in the array.
    /// </summary>
    /// <param name="i">The index of the value to get or set.</param>
    /// <returns>The value at the specified index.</returns>
    public override byte this[int i] {
        get {
            uint offset = IndexToOffset(i);
            return UInt8[offset];
        }
        set {
            uint offset = IndexToOffset(i);
            UInt8[offset] = value;
        }
    }
}