namespace Spice86.Emulator.Function;

using Spice86.Emulator.Memory;
using Spice86.Utils;

using System;
using System.Collections.Generic;

public class FunctionInformation : IComparable<FunctionInformation>
{
    private readonly SegmentedAddress _address;

    private readonly List<FunctionInformation> _callers = new();

    private readonly string? _name;

    private readonly Func<Action>? _overrideRenamed;

    private readonly Dictionary<FunctionReturn, List<SegmentedAddress>> _returns = new();

    private readonly Dictionary<FunctionReturn, List<SegmentedAddress>> _unalignedReturns = new();

    private int _calledCount;

    public FunctionInformation(SegmentedAddress address, string name) : this(address, name, null)
    {
    }

    public FunctionInformation(SegmentedAddress address, string name, Func<Action>? overrideRenamed)
    {
        this._address = address;
        this._name = name;
        this._overrideRenamed = overrideRenamed;
    }

    public void AddReturn(FunctionReturn functionReturn, SegmentedAddress? target)
    {
        AddReturn(_returns, functionReturn, target);
    }

    public void AddUnalignedReturn(FunctionReturn functionReturn, SegmentedAddress? target)
    {
        AddReturn(_unalignedReturns, functionReturn, target);
    }

    public void CallOverride()
    {
        if (HasOverride())
        {
            var retHandler = _overrideRenamed;
            retHandler?.Invoke();
        }
    }

    public int CompareTo(FunctionInformation? other)
    {
        return this.GetAddress().CompareTo(other?.GetAddress());
    }

    public void Enter(FunctionInformation? caller)
    {
        if (caller != null)
        {
            this._callers.Add(caller);
        }

        _calledCount++;
    }

    public override bool Equals(object? obj)
    {
        if (this == obj)
        {
            return true;
        }
        if (obj is not FunctionInformation other)
        {
            return false;
        }
        return _address.Equals(other._address);
    }

    public SegmentedAddress GetAddress()
    {
        return _address;
    }

    public int GetCalledCount()
    {
        return _calledCount;
    }

    public IEnumerable<FunctionInformation> GetCallers()
    {
        return _callers;
    }

    public override int GetHashCode()
    {
        return _address.GetHashCode();
    }

    public string? GetName()
    {
        return _name;
    }

    public Dictionary<FunctionReturn, List<SegmentedAddress>> GetReturns()
    {
        return _returns;
    }

    public Dictionary<FunctionReturn, List<SegmentedAddress>> GetUnalignedReturns()
    {
        return _unalignedReturns;
    }

    public bool HasOverride()
    {
        return _overrideRenamed != null;
    }

    public override string ToString()
    {
        return $"{this._name}_{ConvertUtils.ToCSharpStringWithPhysical(this._address)}";
    }

    private static void AddReturn(Dictionary<FunctionReturn, List<SegmentedAddress>> returnsMap, FunctionReturn functionReturn, SegmentedAddress? target)
    {
        var addresses = returnsMap.GetValueOrDefault(functionReturn, new());
        if (target != null)
        {
            addresses.Add(target);
        }
    }
}