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

    public void Write(uint address, ushort value) {
        byte[] bytes = BitConverter.GetBytes(value);
        foreach (byte item in bytes) {
            Write(address, item);
        }
    }
    
    public void Write(uint address, uint value) {
        byte[] bytes = BitConverter.GetBytes(value);
        foreach (byte item in bytes) {
            Write(address, item);
        }
    }
    
    public Span<byte> GetSpan(int address, int length) {
        return _memory.AsSpan(address, length);
    }

}