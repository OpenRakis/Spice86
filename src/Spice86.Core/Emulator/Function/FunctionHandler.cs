namespace Spice86.Core.Emulator.Function;

using System.Text;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Utils;

/// <summary>
/// Handles function calls for the emulator machine.
/// </summary>
public class FunctionHandler {
    private readonly ILoggerService _loggerService;

    private readonly Stack<FunctionCall> _callerStack = new();

    private readonly bool _recordData;

    private readonly State _state;

    private readonly IMemory _memory;

    private readonly ExecutionFlowRecorder _executionFlowRecorder;

    private uint StackPhysicalAddress => _state.StackPhysicalAddress;

    /// <summary>
    /// Initializes a new instance of the <see cref="FunctionHandler"/> class.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="state">The CPU state.</param>
    /// <param name="executionFlowRecorder">The class that records machine code execution flow.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    /// <param name="recordData">Whether we record execution data. If not, <see cref="Call"/> and <see cref="Ret"/> won't record execution flow.</param>
    public FunctionHandler(IMemory memory, State state, ExecutionFlowRecorder executionFlowRecorder, ILoggerService loggerService, bool recordData) {
        _memory = memory;
        _state = state;
        _executionFlowRecorder = executionFlowRecorder;
        _loggerService = loggerService;
        _recordData = recordData;
    }

    /// <summary>
    /// Calls a function.
    /// </summary>
    /// <param name="callType">The call type.</param>
    /// <param name="entrySegment">The entry segment.</param>
    /// <param name="entryOffset">The entry offset.</param>
    /// <param name="expectedReturnSegment">The expected return segment.</param>
    /// <param name="expectedReturnOffset">The expected return offset.</param>
    public void Call(CallType callType, ushort entrySegment, ushort entryOffset, ushort expectedReturnSegment, ushort expectedReturnOffset) {
        Call(callType, entrySegment, entryOffset, expectedReturnSegment, expectedReturnOffset, null, true);
    }
    
    /// <summary>
    /// Calls a function.
    /// </summary>
    /// <param name="callType">The call type.</param>
    /// <param name="entrySegment">The entry segment.</param>
    /// <param name="entryOffset">The entry offset.</param>
    /// <param name="expectedReturnSegment">The expected return segment.</param>
    /// <param name="expectedReturnOffset">The expected return offset.</param>
    /// <param name="name">The function name.</param>
    /// <param name="recordReturn">Whether the function return is recorded for execution flow analysis.</param>
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

    /// <summary>
    /// Gets or creates information about a machine code function.
    /// </summary>
    /// <param name="entryAddress">The address of the entry point for the function.</param>
    /// <param name="name">The function name.</param>
    /// <returns>The function information representation.</returns>
    public FunctionInformation GetOrCreateFunctionInformation(SegmentedAddress entryAddress, string? name) {
        if (!FunctionInformations.TryGetValue(entryAddress, out FunctionInformation? res)) {
            res = new FunctionInformation(entryAddress, string.IsNullOrWhiteSpace(name) ? "unknown" : name);
            FunctionInformations.Add(entryAddress, res);
        }
        return res;
    }

    /// <summary>
    /// Returns a string representation of the call stack.
    /// </summary>
    /// <returns>A string representation of the call stack.</returns>
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

    /// <summary>
    /// Gets or sets the dictionary that contains information about functions.
    /// </summary>
    public IDictionary<SegmentedAddress, FunctionInformation> FunctionInformations { get; set; } = new Dictionary<SegmentedAddress, FunctionInformation>();

    /// <summary>
    /// Calls an interrupt handler.
    /// </summary>
    /// <param name="callType">The type of the call.</param>
    /// <param name="entrySegment">The segment of the entry point.</param>
    /// <param name="entryOffset">The offset of the entry point.</param>
    /// <param name="expectedReturnSegment">The expected segment of the return address.</param>
    /// <param name="expectedReturnOffset">The expected offset of the return address.</param>
    /// <param name="vectorNumber">The vector number of the interrupt handler.</param>
    /// <param name="recordReturn">A value indicating whether to record the return.</param>
    public void Icall(CallType callType, ushort entrySegment, ushort entryOffset, ushort expectedReturnSegment, ushort expectedReturnOffset, byte vectorNumber, bool recordReturn) {
        Call(callType, entrySegment, entryOffset, expectedReturnSegment, expectedReturnOffset, $"interrupt_handler_{ConvertUtils.ToHex(vectorNumber)}", recordReturn);
    }

    /// <summary>
    /// Returns the return address of the specified call type from the machine stack without removing it.
    /// </summary>
    /// <param name="returnCallType">The type of the return call.</param>
    /// <returns>The return address of the specified call type from the machine stack without removing it.</returns>
    public SegmentedAddress? PeekReturnAddressOnMachineStack(CallType returnCallType) {
        uint stackPhysicalAddress = StackPhysicalAddress;
        return PeekReturnAddressOnMachineStack(returnCallType, stackPhysicalAddress);
    }

    /// <summary>
    /// Returns the return address of the specified call type from the machine stack at the specified physical address without removing it.
    /// </summary>
    /// <param name="returnCallType">The calling convention.</param>
    /// <param name="stackPhysicalAddress">The physical address of the stack.</param>
    /// <returns>The return address of the specified call type from the machine stack at the specified physical address without removing it.</returns>
    public SegmentedAddress? PeekReturnAddressOnMachineStack(CallType returnCallType, uint stackPhysicalAddress) {
        IMemory memory = _memory;
        return returnCallType switch {
            CallType.NEAR => new SegmentedAddress(_state.CS, memory.UInt16[stackPhysicalAddress]),
            CallType.FAR or CallType.INTERRUPT => new SegmentedAddress(memory.SegmentedAddress[stackPhysicalAddress]),
            CallType.MACHINE => null,
            _ => null
        };
    }

    /// <summary>
    /// Returns the return address of the current function from the machine stack without removing it.
    /// </summary>
    /// <returns>The return address of the current function from the machine stack without removing it.</returns>
    public SegmentedAddress? PeekReturnAddressOnMachineStackForCurrentFunction() {
        FunctionCall? currentFunctionCall = CurrentFunctionCall;
        return currentFunctionCall == null ? null : PeekReturnAddressOnMachineStack(currentFunctionCall.Value.CallType);
    }

    /// <summary>
    /// Returns from a call.
    /// </summary>
    /// <param name="returnCallType">The calling convention.</param>
    /// <returns>A value indicating whether the return was successful.</returns>
    public bool Ret(CallType returnCallType) {
        if (!_recordData) {
            return true;
        }

        if (_callerStack.TryPop(out FunctionCall currentFunctionCall) == false) {
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
        ushort cs = _state.CS;
        ushort ip = _state.IP;
        return new FunctionReturn(returnCallType, new SegmentedAddress(cs, ip));
    }

    private FunctionCall? CurrentFunctionCall {
        get {
            if (_callerStack.Count > 0 == false) {
                return null;
            }
            return _callerStack.TryPeek(out FunctionCall firstElement) ? firstElement : null;
        }
    }


    private SegmentedAddress CurrentStackAddress => new(_state.SS, _state.SP);

    private FunctionInformation? GetFunctionInformation(FunctionCall? functionCall) {
        if (functionCall == null) {
            return null;
        }
        if (FunctionInformations.TryGetValue(functionCall.Value.EntryPointAddress, out FunctionInformation? value)) {
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

        // Record the unexpected behaviour. Generated code will see this as well.
        _executionFlowRecorder.RegisterUnalignedReturn(_state.CS, _state.IP, actualReturnAddress.Value.Segment,
            actualReturnAddress.Value.Offset);
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