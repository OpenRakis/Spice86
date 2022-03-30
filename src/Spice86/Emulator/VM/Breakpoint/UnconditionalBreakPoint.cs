namespace Spice86.Emulator.VM.Breakpoint;

using System;

public class UnconditionalBreakPoint : BreakPoint {

    public UnconditionalBreakPoint(BreakPointType breakPointType, Action<BreakPoint> onReached, bool removeOnTrigger) : base(breakPointType, onReached, removeOnTrigger) {
    }

    public override bool Matches(long address) {
        return true;
    }

    public override bool Matches(long startAddress, long endAddress) {
        return true;
    }
}