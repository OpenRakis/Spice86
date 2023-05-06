namespace Spice86.Core.Emulator.Function;

using System;
using Spice86.Shared;
using Spice86.Shared.Emulator.Memory;

public record FunctionReturn(CallType ReturnCallType, SegmentedAddress Address) : IComparable<FunctionReturn> {
    public int CompareTo(FunctionReturn? other) {
        return Address.CompareTo(other?.Address);
    }

    public override string ToString() {
        return $"{ReturnCallType} at {Address}";
    }
}