namespace Spice86.Emulator.Memory;

using System.Text.Json;

/// <summary> Represents a range in memory. </summary>
public class MemoryRange {
    private uint endAddress;

    private string name;

    private uint startAddress;

    public MemoryRange(uint startAddress, uint endAddress, string name) {
        this.startAddress = startAddress;
        this.endAddress = endAddress;
        this.name = name;
    }

    public static MemoryRange FromSegment(ushort segmentStart, ushort length, string name) {
        return FromSegment(segmentStart, 0, length, name);
    }

    public static MemoryRange FromSegment(ushort segment, ushort startOffset, ushort length, string name) {
        uint start = MemoryUtils.ToPhysicalAddress(segment, startOffset);
        uint end = MemoryUtils.ToPhysicalAddress(segment, (ushort)(startOffset + length));
        return new MemoryRange(start, end, name);
    }

    public uint GetEndAddress() {
        return endAddress;
    }

    public string GetName() {
        return name;
    }

    public uint GetStartAddress() {
        return startAddress;
    }

    public bool IsInRange(uint rangeStartAddress, uint rangeEndAddress) {
        return rangeStartAddress <= endAddress && rangeEndAddress >= startAddress;
    }

    public bool IsInRange(uint address) {
        return startAddress <= address && address <= endAddress;
    }

    public void SetEndAddress(uint endAddress) {
        this.endAddress = endAddress;
    }

    public void SetName(string name) {
        this.name = name;
    }

    public void SetStartAddress(uint startAddress) {
        this.startAddress = startAddress;
    }

    public override string ToString() {
        return JsonSerializer.Serialize(this);
    }
}