namespace Ix86.Emulator.Function;

using Ix86.Emulator.Memory;
using Ix86.Utils;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

public class FunctionInformation : IComparable<FunctionInformation>
{
    private SegmentedAddress _address;
    private string? _name;
    private ICheckedSupplier<ICheckedRunnable>? _overrideRenamed;
    private Dictionary<FunctionReturn, List<SegmentedAddress>> _returns = new();
    private Dictionary<FunctionReturn, List<SegmentedAddress>> _unalignedReturns = new();
    private List<FunctionInformation> _callers = new List<FunctionInformation>();
    private int calledCount;
    public FunctionInformation(SegmentedAddress address, string name) : this(address, name, null)
    {
    }

    public FunctionInformation(SegmentedAddress address, string name, ICheckedSupplier<ICheckedRunnable>? overrideRenamed)
    {
        this._address = address;
        this._name = name;
        this._overrideRenamed = overrideRenamed;
    }

    public virtual void Enter(FunctionInformation caller)
    {
        if (caller != null)
        {
            this._callers.Add(caller);
        }

        calledCount++;
    }

    public virtual int GetCalledCount()
    {
        return calledCount;
    }

    public virtual bool HasOverride()
    {
        return _overrideRenamed != null;
    }

    public virtual void CallOverride()
    {
        if (HasOverride())
        {
            ICheckedRunnable? retHandler = _overrideRenamed?.Get();
            retHandler?.Run();
        }
    }

    public virtual void AddUnalignedReturn(FunctionReturn functionReturn, SegmentedAddress target)
    {
        AddReturn(_unalignedReturns, functionReturn, target);
    }

    public virtual void AddReturn(FunctionReturn functionReturn, SegmentedAddress target)
    {
        AddReturn(_returns, functionReturn, target);
    }

    private void AddReturn(Dictionary<FunctionReturn, List<SegmentedAddress>> returnsMap, FunctionReturn functionReturn, SegmentedAddress target)
    {
        if(returnsMap.ContainsKey(functionReturn) == false)
        {
            returnsMap.Add(functionReturn, new List<SegmentedAddress>());
        }
        if(returnsMap.TryGetValue(functionReturn, out var addresses))
        {
            if (target != null)
            {
                addresses.Add(target);
            }
        }
    }

    public virtual string? GetName()
    {
        return _name;
    }

    public virtual SegmentedAddress GetAddress()
    {
        return _address;
    }

    public virtual Dictionary<FunctionReturn, List<SegmentedAddress>> GetReturns()
    {
        return _returns;
    }

    public virtual Dictionary<FunctionReturn, List<SegmentedAddress>> GetUnalignedReturns()
    {
        return _unalignedReturns;
    }

    public virtual IEnumerable<FunctionInformation> GetCallers()
    {
        return _callers;
    }

    public int CompareTo(FunctionInformation? other)
    {
        return this.GetAddress().CompareTo(other?.GetAddress());
    }
}
