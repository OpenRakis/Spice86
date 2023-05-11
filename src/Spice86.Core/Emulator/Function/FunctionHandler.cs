namespace Spice86.Core.Emulator.Function;

using System.Text;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Utils;

public class FunctionHandler {
    private readonly ILoggerService _loggerService;

    private readonly Stack<FunctionCall> _callerStack = new();

    private readonly bool _recordData;

    private readonly Machine _machine;

    private uint StackPhysicalAddress => _machine.Cpu.State.StackPhysicalAddress;
    
    public FunctionHandler(Machine machine, ILoggerService loggerService, bool recordData) {
        _loggerService = loggerService;
        _machine = machine;
        _recordData = recordData;
    }

    public void Call(CallType callType, ushort entrySegment, ushort entryOffset, ushort expectedReturnSegment, ushort expectedReturnOffset) {
        Call(callType, entrySegment, entryOffset, expectedReturnSegment, expectedReturnOffset, null, true);
    }

    public void Call(CallType callType, ushort entrySegment, ushort entryOffset, ushort? expectedReturnSegment, ushort? expectedReturnOffset, string? name, bool recordReturn) {
        SegmentedAddress entryAddress = new(entrySegment, entryOffset);
        FunctionInformation currentFunction = GetOrCreateFunctionInformation(entryAddress, name);
        if (_recordData) {
            FunctionInformation? caller = GetFunctionInformation(CurrentFunctionCall);
            SegmentedAddress? expectedReturnAddress = null;
            if (expectedReturnSegment != null && expectedReturnOffset != null) {
                expectedReturnAddress = new SegmentedAddress(expectedReturnSegment.Value, expectedReturnOffset.Value);
            }

            FunctionCall currentFunctionCall = new(callType, entryAddress, expectedReturnAddress, CurrentStackAddress, recordReturn);
            _callerStack.Push(currentFunctionCall);
            if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                _loggerService.Debug("Calling {CurrentFunction} from {Caller}", currentFunction, caller);
            }

            currentFunction.Enter(caller);
        }

        if (UseCodeOverride) {
            currentFunction.CallOverride();
        }
    }

    public FunctionInformation GetOrCreateFunctionInformation(SegmentedAddress entryAddress, string? name) {
        if (!FunctionInformations.TryGetValue(entryAddress, out FunctionInformation? res)) {
            res = new FunctionInformation(entryAddress, string.IsNullOrWhiteSpace(name) ? "unknown" : name);
            FunctionInformations.Add(entryAddress, res);
        }
        return res;
    }

    public string DumpCallStack() {
        StringBuilder res = new();
        foreach (FunctionCall functionCall in _callerStack) {
            SegmentedAddress? returnAddress = functionCall.ExpectedReturnAddress;
            FunctionInformation? functionInformation = GetFunctionInformation(functionCall);
            res.Append(" - ");
            res.Append(functionInformation);
            res.Append(" expected to return to address ");
            res.Append(returnAddress);
            res.Append('\n');
        }

        return res.ToString();
    }

    public IDictionary<SegmentedAddress, FunctionInformation> FunctionInformations { get; set; } = new Dictionary<SegmentedAddress, FunctionInformation>();

    public void Icall(CallType callType, ushort entrySegment, ushort entryOffset, ushort expectedReturnSegment, ushort expectedReturnOffset, byte vectorNumber, bool recordReturn) {
        Call(callType, entrySegment, entryOffset, expectedReturnSegment, expectedReturnOffset, $"interrupt_handler_{ConvertUtils.ToHex(vectorNumber)}", recordReturn);
    }

    public SegmentedAddress? PeekReturnAddressOnMachineStack(CallType returnCallType) {
        uint stackPhysicalAddress = StackPhysicalAddress;
        return PeekReturnAddressOnMachineStack(returnCallType, stackPhysicalAddress);
    }

    public SegmentedAddress? PeekReturnAddressOnMachineStack(CallType returnCallType, uint stackPhysicalAddress) {
        Memory memory = _machine.Memory;
        State state = _machine.Cpu.State;
        return returnCallType switch {
            CallType.NEAR => new SegmentedAddress(state.CS, memory.GetUint16(stackPhysicalAddress)),
            CallType.FAR or CallType.INTERRUPT => new SegmentedAddress(
                memory.GetUint16(stackPhysicalAddress + 2),
                memory.GetUint16(stackPhysicalAddress)),
            CallType.MACHINE => null,
            _ => null
        };
    }

    public SegmentedAddress? PeekReturnAddressOnMachineStackForCurrentFunction() {
        FunctionCall? currentFunctionCall = CurrentFunctionCall;
        return currentFunctionCall == null ? null : PeekReturnAddressOnMachineStack(currentFunctionCall.CallType);
    }

    public bool Ret(CallType returnCallType) {
        if (!_recordData) {
            return true;
        }

        if (_callerStack.TryPop(out FunctionCall? currentFunctionCall) == false) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("Returning but no call was done before!!");
            }
            return false;
        }
        FunctionInformation? currentFunctionInformation = GetFunctionInformation(currentFunctionCall);
        bool returnAddressAlignedWithCallStack = AddReturn(returnCallType, currentFunctionCall, currentFunctionInformation);
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("Returning from {CurrentFunctionInformation} to {CurrentFunctionCall}", currentFunctionInformation, GetFunctionInformation(CurrentFunctionCall));
        }

        if (!returnAddressAlignedWithCallStack) {
            // Put it back in the stack, we did a jump not a return
            _callerStack.Push(currentFunctionCall);
        }
        return true;
    }

    /// <summary>
    /// Gets or sets whether we call the C# override or the original machine code.
    /// </summary>
    public bool UseCodeOverride { get; set; }

    private bool AddReturn(CallType returnCallType, FunctionCall currentFunctionCall, FunctionInformation? currentFunctionInformation) {
        FunctionReturn currentFunctionReturn = GenerateCurrentFunctionReturn(returnCallType);
        SegmentedAddress? actualReturnAddress = PeekReturnAddressOnMachineStack(returnCallType);
        bool returnAddressAlignedWithCallStack = HandleUnalignedReturns(currentFunctionCall, actualReturnAddress, currentFunctionReturn);
        if (currentFunctionInformation != null && !UseOverride(currentFunctionInformation)) {
            SegmentedAddress? addressToRecord = actualReturnAddress;
            if (!currentFunctionCall.IsReturnRecorded) {
                addressToRecord = null;
            }

            if (returnAddressAlignedWithCallStack) {
                currentFunctionInformation.AddReturn(currentFunctionReturn, addressToRecord);
            } else {
                currentFunctionInformation.AddUnalignedReturn(currentFunctionReturn, addressToRecord);
            }
        }

        return returnAddressAlignedWithCallStack;
    }

    private FunctionReturn GenerateCurrentFunctionReturn(CallType returnCallType) {
        Cpu cpu = _machine.Cpu;
        State state = cpu.State;
        ushort cs = state.CS;
        ushort ip = state.IP;
        return new FunctionReturn(returnCallType, new SegmentedAddress(cs, ip));
    }

    private FunctionCall? CurrentFunctionCall {
        get {
            if (_callerStack.Count > 0 == false) {
                return null;
            }
            return _callerStack.TryPeek(out FunctionCall? firstElement) ? firstElement : null;
        }
    }


    private SegmentedAddress CurrentStackAddress {
        get {
            State state = _machine.Cpu.State;
            return new SegmentedAddress(state.SS, state.SP);
        }
    }

    private FunctionInformation? GetFunctionInformation(FunctionCall? functionCall) {
        if (functionCall == null) {
            return null;
        }
        if (FunctionInformations.TryGetValue(functionCall.EntryPointAddress, out FunctionInformation? value)) {
            return value;
        }
        return null;
    }

    private bool HandleUnalignedReturns(FunctionCall currentFunctionCall, SegmentedAddress? actualReturnAddress, FunctionReturn currentFunctionReturn) {
        SegmentedAddress? expectedReturnAddress = currentFunctionCall.ExpectedReturnAddress;

        // Null check necessary for machine stop call, in this case it won't be equals to what is in
        // the stack but it's expected.
        if (actualReturnAddress == null || actualReturnAddress.Equals(expectedReturnAddress)) {
            // Everything is normal
            return true;
        }
        
        Cpu cpu  = _machine.Cpu;
        State state = cpu.State;
        // Record the unexpected behaviour. Generated code will see this as well.
        cpu.ExecutionFlowRecorder.RegisterUnalignedReturn(state.CS, state.IP, actualReturnAddress.Segment,
            actualReturnAddress.Offset);
        FunctionInformation? currentFunctionInformation = GetFunctionInformation(currentFunctionCall);
        if (_loggerService.IsEnabled(LogEventLevel.Verbose) && currentFunctionInformation != null
            && !currentFunctionInformation.UnalignedReturns.ContainsKey(currentFunctionReturn)) {
            CallType callType = currentFunctionCall.CallType;
            SegmentedAddress stackAddressAfterCall = currentFunctionCall.StackAddressAfterCall;
            SegmentedAddress? returnAddressOnCallTimeStack = PeekReturnAddressOnMachineStack(callType, stackAddressAfterCall.ToPhysical());
            SegmentedAddress currentStackAddress = CurrentStackAddress;
            string additionalInformation = Environment.NewLine;
            if (!currentStackAddress.Equals(stackAddressAfterCall)) {
                int delta = (int)Math.Abs(currentStackAddress.ToPhysical() - (long)stackAddressAfterCall.ToPhysical());
                additionalInformation +=
                    $"Stack is not pointing at the same address as it was at call time. Delta is {delta} bytes{Environment.NewLine}";
            }
            if (!Equals(expectedReturnAddress, returnAddressOnCallTimeStack)) {
                additionalInformation += "Return address on stack was modified";
            }

            if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                _loggerService.Debug(@"PROGRAM IS NOT WELL BEHAVED SO CALL STACK COULD NOT BE TRACEABLE ANYMORE!
                    Current function {CurrentFunctionInformation} return {CurrentFunctionReturn} will not go to the expected place:
                    - At {CallType} call time, return was supposed to be {ExpectedReturnAddress} stored at SS:SP {StackAddressAfterCall}. Value there is now {ReturnAddressOnCallTimeStack}
                    - On the stack it is now {ActualReturnAddress} stored at SS:SP {CurrentStackAddress}
                    {AdditionalInformation}
                ",
                    currentFunctionInformation.ToString(), currentFunctionReturn.ToString(),
                    callType.ToString(), expectedReturnAddress?.ToString(), stackAddressAfterCall.ToString(), returnAddressOnCallTimeStack?.ToString(),
                    actualReturnAddress.ToString(), currentStackAddress.ToString(),
                    additionalInformation);
            }
        }
        return false;
    }

    private bool UseOverride(FunctionInformation? functionInformation) {
        return UseCodeOverride && functionInformation != null && functionInformation.HasOverride;
    }
}