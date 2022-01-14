namespace Spice86.Emulator.Memory;

using System.Text.Json;

/// <summary> Represents a range in memory. </summary>
public class MemoryRange {
    private int endAddress;

    private string name;

    private int startAddress;

    public MemoryRange(int startAddress, int endAddress, string name) {
        this.startAddress = startAddress;
        this.endAddress = endAddress;
        this.name = name;
    }

    public static MemoryRange FromSegment(int segmentStart, int length, string name) {
        return FromSegment(segmentStart, 0, length, name);
    }

    public static MemoryRange FromSegment(int segment, int startOffset, int length, string name) {
        int start = MemoryUtils.ToPhysicalAddress(segment, startOffset);
        int end = MemoryUtils.ToPhysicalAddress(segment, startOffset + length);
        return new MemoryRange(start, end, name);
    }

    public int GetEndAddress() {
        return endAddress;
    }

    public string GetName() {
        return name;
    }

    public int GetStartAddress() {
        return startAddress;
    }

    public bool IsInRange(int rangeStartAddress, int rangeEndAddress) {
        return rangeStartAddress <= endAddress && rangeEndAddress >= startAddress;
    }

    public bool IsInRange(int address) {
        return startAddress <= address && address <= endAddress;
    }

    public void SetEndAddress(int endAddress) {
        this.endAddress = endAddress;
    }

    public void SetName(string name) {
        this.name = name;
    }

    public void SetStartAddress(int startAddress) {
        this.startAddress = startAddress;
    }

    public override string ToString() {
        return JsonSerializer.Serialize(this);
    }
}