namespace Spice86.Core.Emulator.Memory.ReaderWriter;

/// <summary>
/// ByteReaderWriter that wraps another IByteReaderWriter.
/// Reads and writes are done with address + BaseAddress
/// </summary>
public sealed class ByteReaderWriterWithBaseAddress : IByteReaderWriter {
    private readonly IByteReaderWriter _byteReaderWriter;
    private readonly IBaseAddressProvider _baseAddressProvider;

    /// <summary>
    /// Builds a new ByteReaderWriterWithBaseAddress from the given byteReaderWriter providing a data source / store and baseAddressProvider providing an address modifier.
    /// </summary>
    /// <param name="byteReaderWriter">Where data is read and written.</param>
    /// <param name="baseAddressProvider">Provider for the base address that will be added to addresses given for read / write operations</param>
    public ByteReaderWriterWithBaseAddress(IByteReaderWriter byteReaderWriter, IBaseAddressProvider baseAddressProvider) {
        _byteReaderWriter = byteReaderWriter;
        _baseAddressProvider = baseAddressProvider;
    }

    /// <inheritdoc/>
    public int Length => _byteReaderWriter.Length;

    /// <inheritdoc/>
    public byte this[uint address] {
        get => _byteReaderWriter[address + _baseAddressProvider.BaseAddress];
        set => _byteReaderWriter[address + _baseAddressProvider.BaseAddress] = value;
    }

    /// <inheritdoc/>
    public bool TryGetSpan(out uint startAddress, out Span<byte> span) {
        if (_byteReaderWriter.TryGetSpan(_baseAddressProvider.BaseAddress, out span)) {
            startAddress = 0;
            return true;
        }

        startAddress = 0;
        span = [];
        return false;
    }

    /// <inheritdoc/>
    public bool TryGetSpan(uint startAddress, out Span<byte> span) {
        // Note that this may overflow. If that occurs, then just assume that it is a 32-bit address overflow and wrap.
        uint startAddressRebased = startAddress + _baseAddressProvider.BaseAddress;
        if (_byteReaderWriter.TryGetSpan(startAddressRebased, out span)) {
            return true;
        }

        span = [];
        return false;
    }

    /// <inheritdoc/>
    public bool TryGetSpan(uint startAddress, int length, out Span<byte> span) {
        // Note that this may overflow. If that occurs, then just assume that it is a 32-bit address overflow and wrap.
        uint startAddressRebased = startAddress + _baseAddressProvider.BaseAddress;
        if (_byteReaderWriter.TryGetSpan(startAddressRebased, length, out span)) {
            return true;
        }

        // Defer argument check here as it should have already been checked by TryGetSpan() for the success case.
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        span = [];
        return false;
    }
}
