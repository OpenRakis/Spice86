namespace Spice86.Core.Emulator.VM.Breakpoint;

using System;

public abstract class BreakPoint {
    public BreakPoint(BreakPointType breakPointType, Action<BreakPoint> onReached, bool isRemovedOnTrigger) {
        BreakPointType = breakPointType;
        OnReached = onReached;
        IsRemovedOnTrigger = isRemovedOnTrigger;
    }

    public Action<BreakPoint> OnReached { get; private set; }

    public BreakPointType BreakPointType { get; private set; }

    public bool IsRemovedOnTrigger { get; private set; }

    public abstract bool Matches(long address);

    public abstract bool Matches(long startAddress, long endAddress);

    public void Trigger() {
        OnReached.Invoke(this);
    }
}