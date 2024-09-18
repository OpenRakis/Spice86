namespace Spice86.Core.Emulator.CPU.CfgCpu.CallFlow;

using Serilog.Events;

using Spice86.Core.Emulator.CPU.CfgCpu.Feeder;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Interfaces;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;

public class CallFlowHandler {
    private readonly ILoggerService _loggerService;
    private readonly FunctionNames _functionNames;
    private readonly Stack<FunctionCall> _callerStack = new();
    private readonly State _state;
    private readonly CfgNodeFeeder _cfgNodeFeeder;
    private readonly CallStackViewer _callStackViewer;


    public CallFlowHandler(ILoggerService loggerService, FunctionNames functionNames, State state, IMemory memory, CfgNodeFeeder cfgNodeFeeder) {
        _loggerService = loggerService;
        _functionNames = functionNames;
        _state = state;
        _cfgNodeFeeder = cfgNodeFeeder;
        _callStackViewer = new(state, memory);
    }

    private FunctionCall? CurrentFunctionCall {
        get {
            if (_callerStack.Count > 0 == false) {
                return null;
            }
            return _callerStack.TryPeek(out FunctionCall firstElement) ? firstElement : null;
        }
    }

    public void Call(CallType callType, SegmentedAddress entryAddress, SegmentedAddress? expectedReturnAddress, CallInstruction? initiator) {
        FunctionCall currentFunctionCall = new(callType, entryAddress, expectedReturnAddress, _state.StackSegmentedAddress, initiator);
        _callerStack.Push(currentFunctionCall);
    }

    public void Ret(CallType returnCallType) {
        if (_callerStack.TryPop(out FunctionCall currentFunctionCall) == false) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("Returning but no call was done before!!");
            }
            return;
        }
        if (returnCallType == CallType.MACHINE) {
            if (currentFunctionCall.CallType != returnCallType) {
                _loggerService.Warning("Exiting machine entry point but current function does not seem to be entry point!!");
            }
            return;
        }
        SegmentedAddress actualReturnAddress = _callStackViewer.PeekReturnAddressOnMachineStack(returnCallType);
        CfgInstruction instructionAfterReturn = _cfgNodeFeeder.CurrentNodeFromInstructionFeeder;
        if (actualReturnAddress.Equals(currentFunctionCall.ExpectedReturnAddress)) {
            CallInstruction.AddReturn(cfgNodeFeeder.)
            // Everything is normal
            return;
        }
    }
}