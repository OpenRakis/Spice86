namespace Ix86.Emulator.Machine.Breakpoint;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

public class BreakPoint
{
    private readonly BreakPointType _breakPointType;
    private readonly long _address;
    private readonly Action<BreakPoint> _onReached;
    private readonly bool _removeOnTrigger;
    public BreakPoint(BreakPointType breakPointType, long address, Action<BreakPoint> onReached, bool removeOnTrigger)
    {
        this._breakPointType = breakPointType;
        this._address = address;
        this._onReached = onReached;
        this._removeOnTrigger = removeOnTrigger;
    }

    public virtual BreakPointType GetBreakPointType()
    {
        return _breakPointType;
    }

    public virtual long GetAddress()
    {
        return _address;
    }

    public virtual bool IsRemoveOnTrigger()
    {
        return _removeOnTrigger;
    }

    public virtual bool Matches(long address)
    {
        return this._address == address;
    }

    public virtual bool Matches(long startAddress, long endAddress)
    {
        return this._address >= startAddress && this._address < endAddress;
    }

    public virtual void Trigger()
    {
        _onReached.Invoke(this);
    }
}
