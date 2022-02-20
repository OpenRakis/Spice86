namespace Spice86.Emulator.Function;

using Spice86.Emulator.Memory;
using Spice86.Utils;

using System;
using System.Collections.Generic;

public class FunctionInformation : IComparable<FunctionInformation> {
    private readonly SegmentedAddress _address;

    private ISet<FunctionInformation>? _callers;

    private readonly string? _name;

    private readonly Func<Action>? _overrideRenamed;

    private Dictionary<FunctionReturn, ISet<SegmentedAddress>>? _returns;

    private Dictionary<FunctionReturn, ISet<SegmentedAddress>>? _unalignedReturns;

    private int _calledCount;

    public FunctionInformation(SegmentedAddress address, string name) : this(address, name, null) {
    }

    public FunctionInformation(SegmentedAddress address, string name, Func<Action>? overrideRenamed) {
        this._address = address;
        this._name = name;
        this._overrideRenamed = overrideRenamed;
    }

    public void AddReturn(FunctionReturn functionReturn, SegmentedAddress? target) {
        AddReturn(GetReturns(), functionReturn, target);
    }

    public void AddUnalignedReturn(FunctionReturn functionReturn, SegmentedAddress? target) {
        AddReturn(GetUnalignedReturns(), functionReturn, target);
    }

    public void CallOverride() {
        if (HasOverride()) {
            Func<Action>? retHandler = _overrideRenamed;
            retHandler?.Invoke();
        }
    }

    public int CompareTo(FunctionInformation? other) {
        return this.GetAddress().CompareTo(other?.GetAddress());
    }

    public void Enter(FunctionInformation? caller) {
        if (caller != null) {
            this.GetCallers().Add(caller);
        }

        _calledCount++;
    }

    public override bool Equals(object? obj) {
        if (this == obj) {
            return true;
        }
        if (obj is not FunctionInformation other) {
            return false;
        }
        return _address.Equals(other._address);
    }

    public SegmentedAddress GetAddress() {
        return _address;
    }

    public int GetCalledCount() {
        return _calledCount;
    }

    public ISet<FunctionInformation> GetCallers() {
        if (_callers == null) {
            _callers = new HashSet<FunctionInformation>();
        }
        return _callers;
    }

    public override int GetHashCode() {
        return _address.GetHashCode();
    }

    public string? GetName() {
        return _name;
    }

    public Dictionary<FunctionReturn, ISet<SegmentedAddress>> GetReturns() {
        if (_returns == null) {
            _returns = new();
        }
        return _returns;
    }

    public Dictionary<FunctionReturn, ISet<SegmentedAddress>> GetUnalignedReturns() {
        if (_unalignedReturns == null) {
            _unalignedReturns = new();
        }
        return _unalignedReturns;
    }

    public bool HasOverride() {
        return _overrideRenamed != null;
    }

    public override string ToString() {
        return $"{this._name}_{ConvertUtils.ToCSharpStringWithPhysical(this._address)}";
    }

    private static void AddReturn(Dictionary<FunctionReturn, ISet<SegmentedAddress>> returnsMap, FunctionReturn functionReturn, SegmentedAddress? target) {
        if (target == null) {
            return;
        }
        returnsMap.TryGetValue(functionReturn, out ISet<SegmentedAddress>? addresses);
        if (addresses == null) {
            addresses = new HashSet<SegmentedAddress>();
            returnsMap.Add(functionReturn, addresses);
        }
        addresses.Add(target);
    }
}