namespace Spice86.Emulator.VM.Breakpoint;

using System;

public class BreakPoint {
    public BreakPoint(BreakPointType? breakPointType, long address, Action<BreakPoint> onReached, bool isRemovedOnTrigger) {
        BreakPointType = breakPointType;
        Address = address;
        OnReached = onReached;
        IsRemovedOnTrigger = isRemovedOnTrigger;
    }

    public Action<BreakPoint> OnReached { get; private set; }

    public long Address { get; private set; }

    public BreakPointType? BreakPointType { get; private set; }

    public bool IsRemovedOnTrigger { get; private set; }

    public virtual bool Matches(long address) {
        return Address == address;
    }

    public virtual bool Matches(long startAddress, long endAddress) {
        return Address >= startAddress && this.Address < endAddress;
    }

    public void Trigger() {
        OnReached.Invoke(this);
    }
}