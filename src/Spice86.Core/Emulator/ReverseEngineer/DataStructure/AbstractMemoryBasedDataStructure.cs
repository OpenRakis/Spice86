namespace Spice86.Core.Emulator.ReverseEngineer.DataStructure;

using Spice86.Core.Emulator.Memory.Indexable;
using Spice86.Core.Emulator.Memory.Indexer;
using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure.Array;

/// <summary>
/// Provides a base class for memory-based data structures that have a base address.
/// </summary>
public abstract class AbstractMemoryBasedDataStructure : Indexable, IBaseAddressProvider {
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="byteReaderWriter">Where data is read and written.</param>
    protected AbstractMemoryBasedDataStructure(IByteReaderWriter byteReaderWriter) {
        ByteReaderWriter = byteReaderWriter;
        ByteReaderWriterShiftedToBaseAddress = new ByteReaderWriterWithBaseAddress(byteReaderWriter, this);
        (UInt8, UInt16, UInt32, Int8, Int16, Int32, SegmentedAddress) =
            InstantiateIndexersFromByteReaderWriter(ByteReaderWriterShiftedToBaseAddress);
    }


    /// <inheritdoc/>
    public override UInt8Indexer UInt8 {
        get;
    }

    /// <inheritdoc/>
    public override UInt16Indexer UInt16 {
        get;
    }

    /// <inheritdoc/>
    public override UInt32Indexer UInt32 {
        get;
    }

    /// <inheritdoc/>
    public override Int8Indexer Int8 {
        get;
    }

    /// <inheritdoc/>
    public override Int16Indexer Int16 {
        get;
    }

    /// <inheritdoc/>
    public override Int32Indexer Int32 {
        get;
    }

    /// <inheritdoc/>
    public override SegmentedAddressIndexer SegmentedAddress {
        get;
    }

    /// <summary>
    /// Where data are from
    /// </summary>
    public IByteReaderWriter ByteReaderWriter { get; }

    /// <summary>
    /// Where data are from, shifted to base address.
    /// </summary>
    private ByteReaderWriterWithBaseAddress ByteReaderWriterShiftedToBaseAddress { get; }

    /// <summary>
    /// The base address of the data structure.
    /// </summary>
    public abstract uint BaseAddress { get; }

    protected uint ComputeAddressFromOffset(uint offset) {
        return (uint)(BaseAddress + offset);
    }

    /// <summary>
    /// Gets an 8-bit unsigned integer array from the data structure starting at the specified offset and with the specified length.
    /// </summary>
    /// <param name="start">The offset from the base address to start reading values from.</param>
    /// <param name="length">The length of the array.</param>
    /// <returns>The array of uint8 values.</returns>
    public UInt8Array GetUInt8Array(uint start, int length) {
        return new UInt8Array(ByteReaderWriter, ComputeAddressFromOffset(start), length);
    }

    /// <summary>
    /// Gets a uint16 array from the data structure starting at the specified offset and with the specified length.
    /// </summary>
    /// <param name="start">The offset from the base address to start reading values from.</param>
    /// <param name="length">The length of the array.</param>
    /// <returns>The array of uint16 values.</returns>
    public UInt16Array GetUInt16Array(uint start, int length) {
        return new UInt16Array(ByteReaderWriter, ComputeAddressFromOffset(start), length);
    }

    /// <summary>
    /// Gets an 32-bit unsigned integer array from the data structure starting at the specified offset and with the specified length.
    /// </summary>
    /// <param name="start">The offset from the base address to start reading values from.</param>
    /// <param name="length">The length of the array.</param>
    /// <returns>The array of uint8 values.</returns>
    public UInt32Array GetUInt32Array(uint start, int length) {
        return new UInt32Array(ByteReaderWriter, ComputeAddressFromOffset(start), length);
    }

    /// <summary>
    /// Gets a SegmentedAddress array from the data structure starting at the specified offset and with the specified length.
    /// </summary>
    /// <param name="start">The offset from the base address to start reading values from.</param>
    /// <param name="length">The length of the array.</param>
    /// <returns>The array of uint8 values.</returns>
    public SegmentedAddressArray GetSegmentedAddressArray(uint start, int length) {
        return new SegmentedAddressArray(ByteReaderWriter, ComputeAddressFromOffset(start), length);
    }
}