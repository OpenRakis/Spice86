namespace Spice86.Core.Emulator.Memory.ReaderWriter;

/// <summary>
/// ByteReaderWriter that wraps another IByteReaderWriter.
/// Reads and writes are done with address + BaseAddress
/// </summary>
public class ByteReaderWriterWithBaseAddress : IByteReaderWriter {
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
    public uint Length => _byteReaderWriter.Length;

    /// <inheritdoc/>
    public byte this[uint address] {
        get => _byteReaderWriter[address + _baseAddressProvider.BaseAddress];
        set => _byteReaderWriter[address + _baseAddressProvider.BaseAddress] = value;
    }
}