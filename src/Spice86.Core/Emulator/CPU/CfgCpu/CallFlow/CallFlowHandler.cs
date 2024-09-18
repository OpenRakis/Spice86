namespace Spice86.Core.Emulator.CPU.CfgCpu.CallFlow;

using Serilog.Events;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Interfaces;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;

public class CallFlowHandler {
    private readonly ILoggerService _loggerService;
    private readonly Stack<FunctionCall> _callStack = new();
    private readonly IMemory _memory;
    private readonly State _state;
    

    public CallFlowHandler(IMemory memory, State state, ILoggerService loggerService) {
        _memory = memory;
        _state = state;
        _loggerService = loggerService;
    }

    public void Call(CallType callType, SegmentedAddress entryAddress, SegmentedAddress? expectedReturnAddress, CfgInstruction? initiator) {
        FunctionCall currentFunctionCall = new(callType, entryAddress, expectedReturnAddress, _state.StackSegmentedAddress, initiator);
        _callStack.Push(currentFunctionCall);
    }

    public void Ret(CallType returnCallType, IRetInstruction initiator) {
        if (_callStack.TryPop(out FunctionCall currentFunctionCall) == false) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("Returning but no call was done before!!");
            }
            return;
        }
        if (returnCallType == CallType.MACHINE) {
            if (currentFunctionCall.CallType != returnCallType) {
                _loggerService.Warning("Exiting machine entry point but current function does not seem to be entry point!!");
            }
            // Push back the function call, we are still running after all!
            _callStack.Push(currentFunctionCall);
            return;
        }
        initiator.CurrentCorrespondingCallInstruction = currentFunctionCall.Initiator;
        SegmentedAddress? returnAddress = PeekReturnAddressOnMachineStack(returnCallType);
        if (returnAddress == currentFunctionCall.ExpectedReturnAddress) {
            return;
        }
        // Didn't return where it was supposed to. Let's try to find the cause and realign our call stack.
        // First put the stack back to how it was since it could very well be a jump
        _callStack.Push(currentFunctionCall);
        RealignCallStackWithRealStack();
    }

    /// <summary>
    /// Returns the return address of the specified call type from the machine stack without removing it.
    /// </summary>
    /// <param name="returnCallType">The type of the return call.</param>
    /// <returns>The return address of the specified call type from the machine stack without removing it.</returns>
    public SegmentedAddress? PeekReturnAddressOnMachineStack(CallType returnCallType) {
        uint stackPhysicalAddress = _state.StackPhysicalAddress;
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

    private void RealignCallStackWithRealStack() {
        FunctionCall functionCall = _callStack.Peek();
        if (functionCall.StackAddressAfterCall == _state.StackSegmentedAddress) {
            // case where recorded stack address matches current address
            PopCallStackUntilMatchPhysical();
        }
        // Else => I have no idea how to handle that because we don't know what has been stored on the stack, could be near / far / int calls, could be a different call stack
    }

    private void PopCallStackUntilMatchPhysical() {
        int toPop = ComputeNumberOfFunctionCallsThatDiffer();
        _loggerService.Warning("Popping {toPop} from inferred call stack", toPop);
        while (toPop-- > 0) {
            FunctionCall functionCall = _callStack.Pop();
            if (functionCall.Initiator != null) {
                functionCall.Initiator.ReturnWasToOneOfCaller = true;
            }
        }
    }

    private int ComputeNumberOfFunctionCallsThatDiffer() {
        int different = 0;
        foreach (FunctionCall functionCall in _callStack) {
            SegmentedAddress? storeReturnAddress = PeekReturnAddressOnMachineStack(functionCall.CallType, functionCall.StackAddressAfterCall.ToPhysical());
            // Null addresses are addresses that are not stored in machine RAM => treat them as not different so that they are not trashed. 
            if (storeReturnAddress != null && functionCall.ExpectedReturnAddress != storeReturnAddress) {
                different++;
            } else {
                return different;
            }
        }
        return different;
    }
}