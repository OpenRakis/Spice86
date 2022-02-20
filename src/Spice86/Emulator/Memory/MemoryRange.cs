namespace Spice86.Emulator.Memory;

using System.Text.Json;

/// <summary> Represents a range in memory. </summary>
public class MemoryRange {
    private uint _endAddress;

    private string _name;

    private uint _startAddress;

    public MemoryRange(uint startAddress, uint endAddress, string name) {
        _startAddress = startAddress;
        _endAddress = endAddress;
        _name = name;
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
        return _endAddress;
    }

    public string GetName() {
        return _name;
    }

    public uint GetStartAddress() {
        return _startAddress;
    }

    public bool IsInRange(uint rangeStartAddress, uint rangeEndAddress) {
        return rangeStartAddress <= _endAddress && rangeEndAddress >= _startAddress;
    }

    public bool IsInRange(uint address) {
        return _startAddress <= address && address <= _endAddress;
    }

    public void SetEndAddress(uint endAddress) {
        this._endAddress = endAddress;
    }

    public void SetName(string name) {
        this._name = name;
    }

    public void SetStartAddress(uint startAddress) {
        this._startAddress = startAddress;
    }

    public override string ToString() {
        return JsonSerializer.Serialize(this);
    }
}