namespace Spice86.Core.Emulator.ReverseEngineer.DataStructure;

using Spice86.Core.Emulator.Memory.Indexable;
using Spice86.Core.Emulator.Memory.Indexer;
using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure.Array;
using Spice86.Shared.Emulator.Memory;

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
        (UInt8, UInt16, UInt16BigEndian, UInt32, Int8, Int16, Int32, SegmentedAddress16, SegmentedAddress32) =
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
    public override UInt16BigEndianIndexer UInt16BigEndian {
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
    public override SegmentedAddress16Indexer SegmentedAddress16 {
        get;
    }
    
    /// <inheritdoc/>
    public override SegmentedAddress32Indexer SegmentedAddress32 {
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
    public abstract SegmentedAddress BaseAddress { get; }

    /// <summary>
    /// Adds the base address to the specified offset.
    /// </summary>
    /// <param name="offset">The offset to add to the <see cref="BaseAddress"/></param>
    /// <returns>The segmented address of <see cref="BaseAddress"/> + offset</returns>
    protected SegmentedAddress ComputeAddressFromOffset(ushort offset) {
        return BaseAddress.PlusOffset(offset);
    }

    /// <summary>
    /// Gets an 8-bit unsigned integer array from the data structure starting at the specified offset and with the specified length.
    /// </summary>
    /// <param name="offset">The offset from the base address to start reading values from.</param>
    /// <param name="length">The length of the array.</param>
    /// <returns>The array of uint8 values.</returns>
    public UInt8Array GetUInt8Array(ushort offset, int length) {
        return new UInt8Array(ByteReaderWriter, ComputeAddressFromOffset(offset), length);
    }

    /// <summary>
    /// Gets a uint16 array from the data structure starting at the specified offset and with the specified length.
    /// </summary>
    /// <param name="offset">The offset from the base address to start reading values from.</param>
    /// <param name="length">The length of the array.</param>
    /// <returns>The array of uint16 values.</returns>
    public UInt16Array GetUInt16Array(ushort offset, int length) {
        return new UInt16Array(ByteReaderWriter, ComputeAddressFromOffset(offset), length);
    }

    /// <summary>
    /// Gets an 32-bit unsigned integer array from the data structure starting at the specified offset and with the specified length.
    /// </summary>
    /// <param name="offset">The offset from the base address to start reading values from.</param>
    /// <param name="length">The length of the array.</param>
    /// <returns>The array of uint8 values.</returns>
    public UInt32Array GetUInt32Array(ushort offset, int length) {
        return new UInt32Array(ByteReaderWriter, ComputeAddressFromOffset(offset), length);
    }

    /// <summary>
    /// Gets a SegmentedAddress array from the data structure starting at the specified offset and with the specified length.
    /// </summary>
    /// <param name="offset">The offset from the base address to start reading values from.</param>
    /// <param name="length">The length of the array.</param>
    /// <returns>The array of uint8 values.</returns>
    public SegmentedAddressArray GetSegmentedAddressArray(ushort offset, int length) {
        return new SegmentedAddressArray(ByteReaderWriter, ComputeAddressFromOffset(offset), length);
    }

    /// <summary>
    /// Read a string from memory in this data structure.
    /// </summary>
    /// <param name="offset">The offset from the base address to start reading values from.</param>
    /// <param name="maxLength">The maximum string length</param>
    /// <returns>The zero-terminated string retrieved from memory.</returns>
    public string GetZeroTerminatedString(ushort offset, int maxLength) {
        return GetZeroTerminatedString(BaseAddress.PlusOffset(offset), maxLength);
    }

    /// <summary>
    /// Writes a string directly to memory in this data structure.
    /// </summary>
    /// <param name="offset">The offset at which to write the string</param>
    /// <param name="value">The string to write</param>
    /// <param name="maxLength">The maximum length to write</param>
    public void SetZeroTerminatedString(ushort offset, string value, int maxLength) {
        SetZeroTerminatedString(BaseAddress.PlusOffset(offset), value, maxLength);
    }
}