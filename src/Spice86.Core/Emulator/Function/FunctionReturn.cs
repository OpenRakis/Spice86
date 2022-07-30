namespace Spice86.Core.Emulator.Function;

using Spice86.Core.Emulator.Memory;

using System;

public class FunctionReturn : IComparable<FunctionReturn> {
    private readonly SegmentedAddress _instructionAddress;

    private readonly CallType _returnCallType;

    public FunctionReturn(CallType returnCallType, SegmentedAddress instructionAddress) {
        _returnCallType = returnCallType;
        _instructionAddress = instructionAddress;
    }

    public int CompareTo(FunctionReturn? other) {
        return _instructionAddress.CompareTo(other?._instructionAddress);
    }

    public override bool Equals(object? obj) {
        if (this == obj) {
            return true;
        }
        if (obj is not FunctionReturn other) {
            return false;
        }
        return
                _instructionAddress.Equals(other._instructionAddress)
            && _returnCallType.Equals(other._returnCallType);
    }

    public SegmentedAddress Address => _instructionAddress;

    public override int GetHashCode() {
        return HashCode.Combine(_instructionAddress, _returnCallType);
    }

    public CallType ReturnCallType => _returnCallType;

    public override string ToString() {
        return $"{_returnCallType} at {_instructionAddress}";
    }
}