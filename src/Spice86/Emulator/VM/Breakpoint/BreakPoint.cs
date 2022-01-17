namespace Spice86.Emulator.VM.Breakpoint;

using System;

public class BreakPoint {
    private readonly long _address;

    private readonly BreakPointType? _breakPointType;

    private readonly Action<BreakPoint> _onReached;

    private readonly bool _removeOnTrigger;

    public BreakPoint(BreakPointType? breakPointType, long address, Action<BreakPoint> onReached, bool removeOnTrigger) {
        this._breakPointType = breakPointType;
        this._address = address;
        this._onReached = onReached;
        this._removeOnTrigger = removeOnTrigger;
    }

    public long GetAddress() {
        return _address;
    }

    public BreakPointType? GetBreakPointType() {
        return _breakPointType;
    }

    public bool IsRemoveOnTrigger() {
        return _removeOnTrigger;
    }

    public virtual bool Matches(long address) {
        return this._address == address;
    }

    public virtual bool Matches(long startAddress, long endAddress) {
        return this._address >= startAddress && this._address < endAddress;
    }

    public void Trigger() {
        _onReached.Invoke(this);
    }
}