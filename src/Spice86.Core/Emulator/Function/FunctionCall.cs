namespace Spice86.Core.Emulator.Function;

using Spice86.Core.Emulator.Memory;

public class FunctionCall {
    private readonly CallType _callType;

    private readonly SegmentedAddress _entryPointAddress;

    private readonly SegmentedAddress? _expectedReturnAddress;

    private readonly bool _recordReturn;

    private readonly SegmentedAddress _stackAddressAfterCall;

    public FunctionCall(CallType callType, SegmentedAddress entryPointAddress, SegmentedAddress? expectedReturnAddress, SegmentedAddress stackAddressAfterCall, bool recordReturn) {
        _callType = callType;
        _entryPointAddress = entryPointAddress;
        _expectedReturnAddress = expectedReturnAddress;
        _stackAddressAfterCall = stackAddressAfterCall;
        _recordReturn = recordReturn;
    }

    public CallType CallType => _callType;

    public SegmentedAddress EntryPointAddress => _entryPointAddress;

    public SegmentedAddress? ExpectedReturnAddress => _expectedReturnAddress;

    public SegmentedAddress StackAddressAfterCall => _stackAddressAfterCall;

    public bool IsRecordReturn => _recordReturn;

    public override string ToString() {
        return System.Text.Json.JsonSerializer.Serialize(this);
    }
}