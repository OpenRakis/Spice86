using Ix86.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Ix86.Emulator.Memory;

/// <summary>
/// An address that is represented with a real mode segment and an offset.
/// </summary>
public class SegmentedAddress
{
    private int _segment;
    private int _offset;
    public SegmentedAddress(int segment, int offset)
    {
        _segment = segment;
        _offset = offset;
    }

    public virtual int GetSegment()
    {
        return _segment;
    }

    public virtual int GetOffset()
    {
        return _offset;
    }

    public virtual string ToSegmentOffsetRepresentation()
    {
        return ConvertUtils.ToSegmentedAddressRepresentation(_segment, _offset);
    }

    public virtual int ToPhysical()
    {
        return MemoryUtils.ToPhysicalAddress(_segment, _offset);
    }

    public override int GetHashCode()
    {
        return ToPhysical();
    }

    public override string ToString()
    {
        return ToSegmentOffsetRepresentation() + '/' + ConvertUtils.ToHex(ToPhysical());
    }

    public override bool Equals(object? obj)
    {
        if (this == obj)
        {
            return true;
        }
        if (obj is not SegmentedAddress)
        {
            return false;
        }
        var other = (SegmentedAddress)obj;
        return MemoryUtils.ToPhysicalAddress(_segment, _offset) == MemoryUtils.ToPhysicalAddress(other._segment, other._offset);
    }
}
