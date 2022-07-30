namespace Spice86.Core.Emulator.Memory;

using System.Text.Json;

/// <summary> Represents a range in memory. </summary>
public class MemoryRange {

    public MemoryRange(uint startAddress, uint endAddress, string name) {
        StartAddress = startAddress;
        EndAddress = endAddress;
        Name = name;
    }

    public static MemoryRange FromSegment(ushort segmentStart, ushort length, string name) {
        return FromSegment(segmentStart, 0, length, name);
    }

    public static MemoryRange FromSegment(ushort segment, ushort startOffset, ushort length, string name) {
        uint start = MemoryUtils.ToPhysicalAddress(segment, startOffset);
        uint end = MemoryUtils.ToPhysicalAddress(segment, (ushort)(startOffset + length));
        return new MemoryRange(start, end, name);
    }

    public uint EndAddress { get; set; }

    public string? Name { get; set; }

    public uint StartAddress { get; set; }

    public bool IsInRange(uint rangeStartAddress, uint rangeEndAddress) {
        return rangeStartAddress <= EndAddress && rangeEndAddress >= StartAddress;
    }

    public bool IsInRange(uint address) {
        return StartAddress <= address && address <= EndAddress;
    }

    public override string ToString() {
        return JsonSerializer.Serialize(this);
    }
}