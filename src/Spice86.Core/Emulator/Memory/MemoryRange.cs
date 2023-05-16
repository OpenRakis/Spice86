namespace Spice86.Core.Emulator.Memory;

using System.Text.Json;

using Spice86.Shared.Utils;

/// <summary> Represents a range in memory. </summary>
public class MemoryRange {
    /// <summary>
    /// Creates a new instance of the <see cref="MemoryRange"/> class.
    /// </summary>
    /// <param name="startAddress">The starting address of the memory range.</param>
    /// <param name="endAddress">The ending address of the memory range.</param>
    /// <param name="name">The name of the memory range.</param>
    public MemoryRange(uint startAddress, uint endAddress, string name) {
        StartAddress = startAddress;
        EndAddress = endAddress;
        Name = name;
    }

    /// <summary>
    /// Creates a new instance of the <see cref="MemoryRange"/> class from a memory segment.
    /// </summary>
    /// <param name="segmentStart">The memory segment where the range starts.</param>
    /// <param name="length">The length of the memory range.</param>
    /// <param name="name">The name of the memory range.</param>
    /// <returns>A new instance of the <see cref="MemoryRange"/> class.</returns>
    public static MemoryRange FromSegment(ushort segmentStart, ushort length, string name) {
        return FromSegment(segmentStart, 0, length, name);
    }

    /// <summary>
    /// Creates a new instance of the <see cref="MemoryRange"/> class from a memory segment.
    /// </summary>
    /// <param name="segment">The memory segment where the range starts.</param>
    /// <param name="startOffset">The offset within the segment where the range starts.</param>
    /// <param name="length">The length of the memory range.</param>
    /// <param name="name">The name of the memory range.</param>
    /// <returns>A new instance of the <see cref="MemoryRange"/> class.</returns>
    public static MemoryRange FromSegment(ushort segment, ushort startOffset, ushort length, string name) {
        uint start = MemoryUtils.ToPhysicalAddress(segment, startOffset);
        uint end = MemoryUtils.ToPhysicalAddress(segment, (ushort)(startOffset + length));
        return new MemoryRange(start, end, name);
    }

    /// <summary>
    /// The ending address of the memory range.
    /// </summary>
    public uint EndAddress { get; set; }

    /// <summary>
    /// The name of the memory range.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// The starting address of the memory range.
    /// </summary>
    public uint StartAddress { get; set; }

    /// <summary>
    /// Determines whether the memory range intersects a given address range.
    /// </summary>
    /// <param name="rangeStartAddress">The starting address of the range to test against.</param>
    /// <param name="rangeEndAddress">The ending address of the range to test against.</param>
    /// <returns>True if the memory range intersects the given address range, false otherwise.</returns>
    public bool IsInRange(uint rangeStartAddress, uint rangeEndAddress) {
        return rangeStartAddress <= EndAddress && rangeEndAddress >= StartAddress;
    }

    /// <summary>
    /// Determines whether the memory range contains a given address.
    /// </summary>
    /// <param name="address">The address to test.</param>
    /// <returns>True if the memory range contains the given address, false otherwise.</returns>
    public bool IsInRange(uint address) {
        return StartAddress <= address && address <= EndAddress;
    }

    /// <inheritdoc />
    public override string ToString() {
        return JsonSerializer.Serialize(this);
    }
}