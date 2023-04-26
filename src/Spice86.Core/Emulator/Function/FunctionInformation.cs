namespace Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared;
using Spice86.Shared.Utils;

using System;
using System.Collections.Generic;

public record FunctionInformation : IComparable<FunctionInformation> {
    private readonly SegmentedAddress _address;

    private ISet<FunctionInformation>? _callers;

    public Func<int, Action>? FunctionOverride { get; }

    private Dictionary<FunctionReturn, ISet<SegmentedAddress>>? _returns;

    private Dictionary<FunctionReturn, ISet<SegmentedAddress>>? _unalignedReturns;

    public FunctionInformation(SegmentedAddress address, string name) : this(address, name, null) {
    }

    public FunctionInformation(SegmentedAddress address, string name, Func<int, Action>? functionOverride) {
        _address = address;
        Name = name;
        FunctionOverride = functionOverride;
    }

    public void AddReturn(FunctionReturn functionReturn, SegmentedAddress? target) {
        AddReturn(Returns, functionReturn, target);
    }

    public void AddUnalignedReturn(FunctionReturn functionReturn, SegmentedAddress? target) {
        AddReturn(UnalignedReturns, functionReturn, target);
    }

    public void CallOverride() {
        if (HasOverride) {
            Action? retHandler = FunctionOverride?.Invoke(0);
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

        CalledCount++;
    }

    public SegmentedAddress Address => _address;

    public int CalledCount { get; private set; }

    public ISet<FunctionInformation> Callers {
        get {
            _callers ??= new HashSet<FunctionInformation>();
            return _callers;
        }
    }

    public override int GetHashCode() {
        return _address.GetHashCode();
    }

    public string Name { get; }

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

    public bool HasOverride => FunctionOverride != null;

    public override string ToString() {
        return $"{Name}_{ConvertUtils.ToCSharpStringWithPhysical(_address)}";
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