namespace Spice86.Core.Emulator.Function;

using Spice86.Core.Emulator.Memory;
using Spice86.Shared;
using Spice86.Shared.Emulator.Memory;

public record FunctionCall(CallType CallType, SegmentedAddress EntryPointAddress, SegmentedAddress? ExpectedReturnAddress, SegmentedAddress StackAddressAfterCall, bool IsRecordReturn) {
    public override string ToString() {
        return System.Text.Json.JsonSerializer.Serialize(this);
    }
}