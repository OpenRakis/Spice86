namespace Spice86.Core.Emulator.ReverseEngineer;

using Spice86.Core.Emulator.Memory;
/// <summary>
/// Represents an array of unsigned 8-bit integers stored in memory.
/// </summary>
public class Uint8Array : MemoryBasedArray<byte> {
    /// <summary>
    /// Initializes a new instance of the <see cref="Uint8Array"/> class.
    /// </summary>
    /// <param name="memory">The memory where the array is stored.</param>
    /// <param name="baseAddress">The base address of the array in memory.</param>
    /// <param name="length">The length of the array.</param>
    public Uint8Array(Memory memory, uint baseAddress, int length) : base(memory, baseAddress, length) {
    }

    /// <summary>
    /// Gets the value at the specified index in the array.
    /// </summary>
    /// <param name="index">The index of the value to get.</param>
    /// <returns>The value at the specified index.</returns>
    public override byte GetValueAt(int index) {
        int offset = IndexToOffset(index);
        return GetUint8(offset);
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
        get { return GetValueAt(i); }
        set { SetValueAt(i, value); }
    }

    /// <summary>
    /// Sets the value at the specified index in the array.
    /// </summary>
    /// <param name="index">The index of the value to set.</param>
    /// <param name="value">The value to set.</param>
    public override void SetValueAt(int index, byte value) {
        int offset = IndexToOffset(index);
        SetUint8(offset, value);
    }
}
