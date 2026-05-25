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

    /// <inheritdoc/>
    public bool TryGetSpan(out uint startAddress, out Span<byte> span, MemoryAccess access)
        => MemoryDeviceUtils.TryGetSpan(_memory, out startAddress, out span);

    /// <inheritdoc/>
    public bool TryGetSpan(out uint startAddress, out ReadOnlySpan<byte> span, MemoryAccess access)
        => MemoryDeviceUtils.TryGetSpan(_memory, out startAddress, out span);

    /// <inheritdoc/>
    public bool TryGetSpan(uint startAddress, out Span<byte> span, MemoryAccess access)
        => MemoryDeviceUtils.TryGetSpan(_memory, startAddress, out span);

    /// <inheritdoc/>
    public bool TryGetSpan(uint startAddress, out ReadOnlySpan<byte> span, MemoryAccess access)
        => MemoryDeviceUtils.TryGetSpan(_memory, startAddress, out span);

    /// <inheritdoc/>
    public bool TryGetSpan(uint startAddress, int length, out Span<byte> span, MemoryAccess access)
        => MemoryDeviceUtils.TryGetSpan(_memory, startAddress, length, out span);

    /// <inheritdoc/>
    public bool TryGetSpan(uint startAddress, int length, out ReadOnlySpan<byte> span, MemoryAccess access)
        => MemoryDeviceUtils.TryGetSpan(_memory, startAddress, length, out span);
}