namespace Spice86.Core.Emulator.Function;

using System;
using Spice86.Shared.Emulator.Memory;
/// <summary>
/// Represents a function return in the emulator, including information about the return call type and the return address.
/// </summary>
/// <param name="ReturnCallType">The calling convention.</param>
/// <param name="Address">The return address.</param>
public record FunctionReturn(CallType ReturnCallType, SegmentedAddress Address) : IComparable<FunctionReturn> {
    /// <inheritdoc/>
    public int CompareTo(FunctionReturn? other) {
        return Address.CompareTo(other?.Address);
    }
    
    /// <inheritdoc/>
    public override string ToString() {
        return $"{ReturnCallType} at {Address}";
    }
}
