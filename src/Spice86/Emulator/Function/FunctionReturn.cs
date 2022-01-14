namespace Spice86.Emulator.Function;

using Spice86.Emulator.Memory;

using System;

public class FunctionReturn : IComparable<FunctionReturn> {
    private readonly SegmentedAddress _instructionAddress;

    private readonly CallType _returnCallType;

    public FunctionReturn(CallType returnCallType, SegmentedAddress instructionAddress) {
        this._returnCallType = returnCallType;
        this._instructionAddress = instructionAddress;
    }

    public int CompareTo(FunctionReturn? other) {
        return this._instructionAddress.CompareTo(other?._instructionAddress);
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

    public SegmentedAddress GetAddress() {
        return _instructionAddress;
    }

    public override int GetHashCode() {
        return HashCode.Combine(_instructionAddress, _returnCallType);
    }

    public CallType GetReturnCallType() {
        return _returnCallType;
    }

    public override string ToString() {
        return $"{_returnCallType} at {_instructionAddress}";
    }
}