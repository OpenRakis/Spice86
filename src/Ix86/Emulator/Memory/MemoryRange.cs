namespace Ix86.Emulator.Memory;
using System.Text.Json;

/// <summary>
/// Represents a range in memory.
/// </summary>
public class MemoryRange
{
    private int startAddress;
    private int endAddress;
    private string name;
    public MemoryRange(int startAddress, int endAddress, string name)
    {
        this.startAddress = startAddress;
        this.endAddress = endAddress;
        this.name = name;
    }

    public virtual int GetStartAddress()
    {
        return startAddress;
    }

    public virtual void SetStartAddress(int startAddress)
    {
        this.startAddress = startAddress;
    }

    public virtual int GetEndAddress()
    {
        return endAddress;
    }

    public virtual void SetEndAddress(int endAddress)
    {
        this.endAddress = endAddress;
    }

    public virtual string GetName()
    {
        return name;
    }

    public virtual void SetName(string name)
    {
        this.name = name;
    }

    public virtual bool IsInRange(int rangeStartAddress, int rangeEndAddress)
    {
        return rangeStartAddress <= endAddress && rangeEndAddress >= startAddress;
    }

    public virtual bool IsInRange(int address)
    {
        return startAddress <= address && address <= endAddress;
    }

    public static MemoryRange FromSegment(int segmentStart, int length, string name)
    {
        return FromSegment(segmentStart, 0, length, name);
    }

    public static MemoryRange FromSegment(int segment, int startOffset, int length, string name)
    {
        int start = MemoryUtils.ToPhysicalAddress(segment, startOffset);
        int end = MemoryUtils.ToPhysicalAddress(segment, startOffset + length);
        return new MemoryRange(start, end, name);
    }

    public override string ToString()
    {
        return JsonSerializer.Serialize(this);
    }
}
