namespace Spice86.Core.Emulator.Function;

using System.Text;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Interfaces;
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

    private readonly State _state;

    private readonly IMemory _memory;

    private readonly ExecutionFlowRecorder? _executionFlowRecorder;

    private uint StackPhysicalAddress => _state.StackPhysicalAddress;

    private readonly FunctionCatalogue _functionCatalogue;

    /// <summary>
    /// Initializes a new instance of the <see cref="FunctionHandler"/> class.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="state">The CPU state.</param>
    /// <param name="functionCatalogue">List of all functions.</param>
    /// <param name="executionFlowRecorder">The class that records machine code execution flow.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public FunctionHandler(IMemory memory, State state, ExecutionFlowRecorder? executionFlowRecorder, FunctionCatalogue functionCatalogue, ILoggerService loggerService) {
        _memory = memory;
        _state = state;
        _executionFlowRecorder = executionFlowRecorder;
        _loggerService = loggerService;
        _functionCatalogue = functionCatalogue;
    }

    
    /// <summary>
    /// Calls an interrupt handler.
    /// </summary>
    /// <param name="entryAddress">The address of the entry point.</param>
    /// <param name="expectedReturnAddress">The expected address of return.</param>
    /// <param name="call">Instruction that initiated the call.</param>
    /// <param name="vectorNumber">The vector number of the interrupt handler.</param>
    /// <param name="recordReturn">A value indicating whether to record the return.</param>
    public void ICall(SegmentedAddress entryAddress, SegmentedAddress expectedReturnAddress, CfgInstruction? call, byte vectorNumber, bool recordReturn) {
        Call(CallType.INTERRUPT, entryAddress, expectedReturnAddress, call, $"interrupt_handler_{ConvertUtils.ToHex(vectorNumber)}", recordReturn);
    }

    /// <summary>
    /// Calls a function
    /// </summary>
    /// <param name="callType">The type of the call.</param>
    /// <param name="entryAddress">The address of the entry point.</param>
    /// <param name="expectedReturnAddress">The expected address of return.</param>
    /// <param name="call">Instruction that initiated the call.</param>
    public void Call(CallType callType, SegmentedAddress entryAddress, SegmentedAddress? expectedReturnAddress, CfgInstruction? call) {
        Call(callType, entryAddress, expectedReturnAddress, call, null, true);
    }

    /// <summary>
    /// Calls a function
    /// </summary>
    /// <param name="callType">The type of the call.</param>
    /// <param name="entryAddress">The address of the entry point.</param>
    /// <param name="expectedReturnAddress">The expected address of return.</param>
    /// <param name="call">Instruction that initiated the call.</param>
    /// <param name="name">Name to give to the function.</param>
    /// <param name="recordReturn">A value indicating whether to record the return.</param>
    public void Call(CallType callType, SegmentedAddress entryAddress, SegmentedAddress? expectedReturnAddress, CfgInstruction? call, string? name, bool recordReturn) {
        FunctionInformation currentFunction = _functionCatalogue.GetOrCreateFunctionInformation(entryAddress, name);
        FunctionCall currentFunctionCall = new(callType, entryAddress, expectedReturnAddress, CurrentStackAddress, call, recordReturn);
        _callerStack.Push(currentFunctionCall);
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            FunctionInformation? caller = _functionCatalogue.GetFunctionInformation(CurrentFunctionCall);
            _loggerService.Verbose("{Depth} Calling {CurrentFunction} from {Caller}", _callerStack.Count, currentFunction, caller);
        }

        if (_executionFlowRecorder != null) {
            FunctionInformation? caller = _functionCatalogue.GetFunctionInformation(CurrentFunctionCall);
            currentFunction.Enter(caller);
        }

        if (UseCodeOverride) {
            currentFunction.CallOverride();
        }
    }

    /// <summary>
    /// Returns a string representation of the call stack.
    /// </summary>
    /// <returns>A string representation of the call stack.</returns>
    public string DumpCallStack() {
        StringBuilder res = new();
        foreach (FunctionCall functionCall in _callerStack) {
            SegmentedAddress? returnAddress = functionCall.ExpectedReturnAddress;
            FunctionInformation? functionInformation = _functionCatalogue.GetFunctionInformation(functionCall);
            res.Append(" - ");
            res.Append(functionInformation);
            res.Append(" expected to return to address ");
            res.Append(returnAddress);
            res.Append('\n');
        }

        return res.ToString();
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
            CallType.FAR or CallType.INTERRUPT or CallType.EXTERNAL_INTERRUPT => memory.SegmentedAddress[stackPhysicalAddress],
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

    public string PeekReturn() => SegmentedAddress.ToString(PeekReturnAddressOnMachineStackForCurrentFunction());

    /// <summary>
    /// Returns from a call.
    /// </summary>
    /// <param name="returnCallType">The calling convention.</param>
    /// <param name="ret">The instruction that initiated this return.</param>
    /// <returns>A value indicating whether the return was successful.</returns>
    public void Ret(CallType returnCallType, IReturnInstruction? ret) {
        if (_callerStack.TryPop(out FunctionCall currentFunctionCall) == false) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("Returning but no call was done before!!. Instruction is {Instruction}", ret);
            }
            return;
        }
        if (returnCallType == CallType.MACHINE) {
            if (currentFunctionCall.CallType != returnCallType) {
                _loggerService.Warning("Exiting machine entry point but current function does not seem to be entry point.");
            }
            return;
        }
        if (ret != null) {
            // Register the call instruction that probably caused this return
            ret.CurrentCorrespondingCallInstruction = currentFunctionCall.Initiator;
        }

        
        bool returnAddressAlignedWithCallStack = HandleReturn(returnCallType, currentFunctionCall);
        if (!returnAddressAlignedWithCallStack) {
            // Put it back in the stack, we did a jump not a return
            _callerStack.Push(currentFunctionCall);
        }
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            FunctionInformation? currentFunctionInformation = _functionCatalogue.GetFunctionInformation(currentFunctionCall);
            _loggerService.Verbose("Returning from function {From} to function {To} ({TargetAddress})", 
                currentFunctionInformation,
                _functionCatalogue.GetFunctionInformation(CurrentFunctionCall),
                PeekReturnAddressOnMachineStack(returnCallType));
        }
    }

    /// <summary>
    /// Gets or sets whether we call the C# override or the original machine code.
    /// </summary>
    public bool UseCodeOverride { get; set; }

    private bool HandleReturn(CallType returnCallType, FunctionCall currentFunctionCall) {
        bool returnAddressAlignedWithCallStack = DetectUnalignedReturns(currentFunctionCall, returnCallType);
        if (_executionFlowRecorder == null) {
            return returnAddressAlignedWithCallStack;
        }
        FunctionInformation? currentFunctionInformation = _functionCatalogue.GetFunctionInformation(currentFunctionCall);
        if (currentFunctionInformation != null && !UseOverride(currentFunctionInformation)) {
            FunctionReturn currentFunctionReturn = new FunctionReturn(returnCallType, _state.IpSegmentedAddress);
            SegmentedAddress? actualReturnAddress = PeekReturnAddressOnMachineStack(returnCallType);
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

    private FunctionCall? CurrentFunctionCall {
        get {
            if (_callerStack.Count > 0 == false) {
                return null;
            }
            return _callerStack.TryPeek(out FunctionCall firstElement) ? firstElement : null;
        }
    }


    private SegmentedAddress CurrentStackAddress => new(_state.SS, _state.SP);

    private bool DetectUnalignedReturns(FunctionCall currentFunctionCall, CallType returnCallType) {
        SegmentedAddress? expectedReturnAddress = currentFunctionCall.ExpectedReturnAddress;
        SegmentedAddress? actualReturnAddress = PeekReturnAddressOnMachineStack(returnCallType);
        // Null check necessary for machine stop call, in this case it won't be equals to what is in
        // the stack but it's expected.
        if (actualReturnAddress == null || actualReturnAddress.Equals(expectedReturnAddress)) {
            // Everything is normal
            return true;
        }

        // Record the unexpected behaviour. Generated code will see this as well.
        _executionFlowRecorder?.RegisterUnalignedReturn(_state.CS, _state.IP, actualReturnAddress.Value.Segment,
            actualReturnAddress.Value.Offset);
        FunctionInformation? currentFunctionInformation = _functionCatalogue.GetFunctionInformation(currentFunctionCall);
        FunctionReturn currentFunctionReturn = new FunctionReturn(returnCallType, _state.IpSegmentedAddress);
        // TODO: increase log level when regular cpu is not there anymore. CfgCpu is quite accurate at detecting this, regular cpu not so much.
        if (_loggerService.IsEnabled(LogEventLevel.Verbose) && currentFunctionInformation != null
            && !currentFunctionInformation.UnalignedReturns.ContainsKey(currentFunctionReturn)) {
            CallType callType = currentFunctionCall.CallType;
            SegmentedAddress stackAddressAfterCall = currentFunctionCall.StackAddressAfterCall;
            SegmentedAddress? returnAddressOnCallTimeStack = PeekReturnAddressOnMachineStack(callType, stackAddressAfterCall.Linear);
            SegmentedAddress currentStackAddress = CurrentStackAddress;
            string additionalInformation = Environment.NewLine;
            if (!currentStackAddress.Equals(stackAddressAfterCall)) {
                int delta = (int)Math.Abs(currentStackAddress.Linear - (long)stackAddressAfterCall.Linear);
                additionalInformation +=
                    $"Stack is not pointing at the same address as it was at call time. Delta is {delta} bytes{Environment.NewLine}";
            }
            if (!Equals(expectedReturnAddress, returnAddressOnCallTimeStack)) {
                additionalInformation += "Return address on stack was modified";
            }

            _loggerService.Verbose(@"PROGRAM IS NOT WELL BEHAVED SO CALL STACK COULD NOT BE TRACEABLE ANYMORE!
Current function {CurrentFunctionInformation} return instruction {CurrentFunctionReturn} will not go to the expected place:
- Expected return at {CallType} call time was {ExpectedReturnAddress} but return will go to {ActualReturnAddress}
- Return was stored on stack at address {StackAddressAfterCall} which contains {ReturnAddressOnCallTimeStack}
- Stack is now at address {CurrentStackAddress}
{AdditionalInformation}
            ",
                currentFunctionInformation.ToString(), currentFunctionReturn.ToString(),
                callType.ToString(), expectedReturnAddress?.ToString(), actualReturnAddress.ToString(), stackAddressAfterCall.ToString(), returnAddressOnCallTimeStack?.ToString(),
                currentStackAddress.ToString(),
                additionalInformation);
        }
        return false;
    }

    private bool UseOverride(FunctionInformation? functionInformation) {
        return UseCodeOverride && functionInformation != null && functionInformation.HasOverride;
    }
}