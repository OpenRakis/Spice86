namespace Spice86.Core.Emulator.Memory;

/// <summary>
///     Represents plain old RAM.
/// </summary>
public class Ram : IMemoryDevice {
    private readonly byte[] _memory;

    public Ram(uint size) {
        _memory = new byte[size];
    }

    public uint Size => (uint)_memory.Length;

    public byte Read(uint address) {
        return _memory[address];
    }

    public void Write(uint address, byte value) {
        _memory[address] = value;
    }

    public Span<byte> GetSpan(int address, int length) {
        return _memory.AsSpan(address, length);
    }
}