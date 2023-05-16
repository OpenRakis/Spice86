namespace Spice86.Core.Emulator.ReverseEngineer;

using Spice86.Core.Emulator.Memory;
/// <summary>
/// Represents an array of 16-bit unsigned integers stored in memory.
/// </summary>
public class Uint16Array : MemoryBasedArray<ushort> {
    /// <summary>
    /// Initializes a new instance of the <see cref="Uint16Array"/> class with the specified memory, base address, and length.
    /// </summary>
    /// <param name="memory">The memory where the array is stored.</param>
    /// <param name="baseAddress">The base address of the array.</param>
    /// <param name="length">The length of the array.</param>
    public Uint16Array(Memory memory, uint baseAddress, int length) : base(memory, baseAddress, length) {
    }

    /// <summary>
    /// Gets the value at the specified index in the array.
    /// </summary>
    /// <param name="index">The zero-based index of the value to get.</param>
    /// <returns>The value at the specified index.</returns>
    public override ushort GetValueAt(int index) {
        int offset = IndexToOffset(index);
        return GetUint16(offset);
    }

    /// <summary>
    /// Gets the size of each value in the array, in bytes.
    /// </summary>
    public override int ValueSize => 2;

    /// <summary>
    /// Gets or sets the value at the specified index in the array.
    /// </summary>
    /// <param name="i">The zero-based index of the value to get or set.</param>
    /// <returns>The value at the specified index.</returns>
    public override ushort this[int i] {
        get { return GetValueAt(i); }
        set { SetValueAt(i, value); }
    }

    /// <summary>
    /// Sets the value at the specified index in the array.
    /// </summary>
    /// <param name="index">The zero-based index of the value to set.</param>
    /// <param name="value">The value to set.</param>
    public override void SetValueAt(int index, ushort value) {
        int offset = IndexToOffset(index);
        SetUint16(offset, value);
    }
}
