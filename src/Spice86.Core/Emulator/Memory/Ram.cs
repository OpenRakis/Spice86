namespace Spice86.Core.Emulator.Memory;

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
    public Span<byte> GetSpan(int address, int length) {
        return _memory.AsSpan(address, length);
    }
}