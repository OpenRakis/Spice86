namespace Spice86.Core.Emulator.Memory;

using Spice86.Shared.Utils;

/// <summary>
/// Represents plain old RAM.
/// </summary>
public class Ram : IMemoryDevice {
    private readonly byte[] _memory;

    /// <summary>
    /// Initializes a new instance of the Ram class with the specified size.
    /// </summary>
    /// <param name="size">The size of the RAM in bytes.</param>
    public Ram(uint size) {
        _memory = new byte[size];
    }

    /// <inheritdoc />
    public uint Size => (uint)_memory.Length;

    /// <inheritdoc />
    public byte Read(uint address) {
        return _memory[address];
    }

    /// <inheritdoc />
    public void Write(uint address, byte value) {
        _memory[address] = value;
    }

    /// <inheritdoc />
    public IList<byte> GetSlice(int address, int length) {
        return _memory.GetSlice(address, length);
    }

    /// <summary>
    /// The bytes of the RAM, up to and including its last non-zero byte. Empty when every byte is zero.
    /// </summary>
    /// <returns>A copy of the bytes.</returns>
    public byte[] CopyContent() {
        int length = _memory.AsSpan().LastIndexOfAnyExcept((byte)0) + 1;
        return _memory.AsSpan(0, length).ToArray();
    }

    /// <summary>
    /// Replaces the content of the RAM with the bytes given. The bytes after them become zero.
    /// </summary>
    /// <param name="content">The new first bytes, at most <see cref="Size"/> of them.</param>
    public void LoadContent(ReadOnlySpan<byte> content) {
        content.CopyTo(_memory);
        _memory.AsSpan(content.Length).Clear();
    }
}
