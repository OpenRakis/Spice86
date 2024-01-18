namespace Spice86.Core.Emulator.Function;

using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Represents a function call in the emulator, including information about the call type, entry point address, expected return address, stack address after the call, and whether the function return is recorded for execution flow analysis
/// </summary>
public readonly record struct FunctionCall(CallType CallType, SegmentedAddress EntryPointAddress, SegmentedAddress? ExpectedReturnAddress, SegmentedAddress StackAddressAfterCall, bool IsReturnRecorded) {
    /// <summary>
    /// Returns a JSON-serialized string representation of the FunctionCall record.
    /// </summary>
    /// <returns>A JSON-serialized string representation of the FunctionCall record.</returns>
    public override string ToString() {
        return System.Text.Json.JsonSerializer.Serialize(this);
    }
}