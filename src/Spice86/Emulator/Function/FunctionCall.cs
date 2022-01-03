namespace Spice86.Emulator.Function;

using Spice86.Emulator.Memory;

public class FunctionCall
{
    private readonly CallType _callType;
    private readonly SegmentedAddress _entryPointAddress;
    private readonly SegmentedAddress? _expectedReturnAddress;
    private readonly SegmentedAddress _stackAddressAfterCall;
    private readonly bool _recordReturn;
    public FunctionCall(CallType callType, SegmentedAddress entryPointAddress, SegmentedAddress? expectedReturnAddress, SegmentedAddress stackAddressAfterCall, bool recordReturn) : base()
    {
        this._callType = callType;
        this._entryPointAddress = entryPointAddress;
        this._expectedReturnAddress = expectedReturnAddress;
        this._stackAddressAfterCall = stackAddressAfterCall;
        this._recordReturn = recordReturn;
    }

    public virtual CallType GetCallType()
    {
        return _callType;
    }

    public virtual SegmentedAddress GetEntryPointAddress()
    {
        return _entryPointAddress;
    }

    public virtual SegmentedAddress? GetExpectedReturnAddress()
    {
        return _expectedReturnAddress;
    }

    public virtual SegmentedAddress GetStackAddressAfterCall()
    {
        return _stackAddressAfterCall;
    }

    public virtual bool IsRecordReturn()
    {
        return _recordReturn;
    }

    public override string ToString()
    {
        return System.Text.Json.JsonSerializer.Serialize(this);
    }
}
