namespace Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Utils;

using System;
using System.Collections.Generic;

public class FunctionInformation : IComparable<FunctionInformation> {
    private readonly SegmentedAddress _address;

    private ISet<FunctionInformation>? _callers;

    private readonly string _name;

    public Func<int, Action>? FuntionOverride { get; set; }

    private Dictionary<FunctionReturn, ISet<SegmentedAddress>>? _returns;

    private Dictionary<FunctionReturn, ISet<SegmentedAddress>>? _unalignedReturns;

    private int _calledCount;

    public FunctionInformation(SegmentedAddress address, string name) : this(address, name, null) {
    }

    public FunctionInformation(SegmentedAddress address, string name, Func<int, Action>? funtionOverride) {
        _address = address;
        _name = name;
        FuntionOverride = funtionOverride;
    }

    public void AddReturn(FunctionReturn functionReturn, SegmentedAddress? target) {
        AddReturn(Returns, functionReturn, target);
    }

    public void AddUnalignedReturn(FunctionReturn functionReturn, SegmentedAddress? target) {
        AddReturn(UnalignedReturns, functionReturn, target);
    }

    public void CallOverride() {
        if (HasOverride) {
            Action? retHandler = FuntionOverride?.Invoke(0);
            // The override returns what to do when going back to emu mode, so let's do it!
            retHandler?.Invoke();
        }
    }

    public int CompareTo(FunctionInformation? other) {
        return Address.CompareTo(other?.Address);
    }

    public void Enter(FunctionInformation? caller) {
        if (caller != null) {
            Callers.Add(caller);
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

    public SegmentedAddress Address => _address;

    public int CalledCount => _calledCount;

    public ISet<FunctionInformation> Callers {
        get {
            _callers ??= new HashSet<FunctionInformation>();
            return _callers;
        }
    }

    public override int GetHashCode() {
        return _address.GetHashCode();
    }

    public string Name => _name;

    public Dictionary<FunctionReturn, ISet<SegmentedAddress>> Returns {
        get {
            _returns ??= new();
            return _returns;
        }
    }

    public Dictionary<FunctionReturn, ISet<SegmentedAddress>> UnalignedReturns {
        get {
            _unalignedReturns ??= new();
            return _unalignedReturns;
        }
    }

    public bool HasOverride => FuntionOverride != null;

    public override string ToString() {
        return $"{_name}_{ConvertUtils.ToCSharpStringWithPhysical(_address)}";
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