using Ix86.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Ix86.Emulator.Memory
{
    /// <summary>
    /// An address that is represented with a real mode segment and an offset.
    /// </summary>
    public class SegmentedAddress
    {
        private int segment;
        private int offset;
        public SegmentedAddress(int segment, int offset)
        {
            this.segment = segment;
            this.offset = offset;
        }

        public virtual int GetSegment()
        {
            return segment;
        }

        public virtual int GetOffset()
        {
            return offset;
        }

        public virtual string ToSegmentOffsetRepresentation()
        {
            return ConvertUtils.ToSegmentedAddressRepresentation(segment, offset);
        }

        public virtual int ToPhysical()
        {
            return MemoryUtils.ToPhysicalAddress(segment, offset);
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
            if(this == obj)
            {
                return true;
            }
            if(obj is not SegmentedAddress)
            {
                return false;
            }
            var other = (SegmentedAddress)obj;
            return MemoryUtils.ToPhysicalAddress(segment, offset) == MemoryUtils.ToPhysicalAddress(other.segment, other.offset);
        }
    }
}