namespace Spice86.Core.Emulator.Function;

using System.Text;

using Microsoft.Extensions.Logging;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Emulator.Errors;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Utils;

/// <summary>
/// Handles function calls for the emulator machine.
/// </summary>
public class FunctionHandler {
    private const int MaxCallStackDepth = 1000;

    private readonly ILogger _loggerService;

    private readonly FunctionCallStack _callerStack = new();

    private readonly State _state;

    private readonly IMemory _memory;

    private readonly FunctionCatalogue _functionCatalogue;

    /// <summary>
    /// Initializes a new instance of the <see cref="FunctionHandler"/> class.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="state">The CPU state.</param>
    /// <param name="functionCatalogue">List of all functions.</param>
    /// <param name="useCodeOverride">Whether or not to call overrides.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public FunctionHandler(
        IMemory memory,
        State state,
        FunctionCatalogue functionCatalogue,
        bool useCodeOverride,
        ILogger loggerService) {
        _memory = memory;
        _state = state;
        _loggerService = loggerService;
        _functionCatalogue = functionCatalogue;
        UseCodeOverride = useCodeOverride;
    }


    /// <summary>
    /// Calls an interrupt handler.
    /// </summary>
    /// <param name="entryAddress">The address of the entry point.</param>
    /// <param name="expectedReturnAddress">The expected address of return.</param>
    /// <param name="call">Instruction that initiated the call.</param>
    /// <param name="vectorNumber">The vector number of the interrupt handler.</param>
    public void ICall(SegmentedAddress entryAddress, SegmentedAddress expectedReturnAddress, CfgInstruction? call, byte vectorNumber) {
        Call(CallType.INTERRUPT, entryAddress, expectedReturnAddress, call, $"interrupt_handler_{ConvertUtils.ToHex(vectorNumber)}");
    }

    /// <summary>
    /// Calls a function
    /// </summary>
    /// <param name="callType">The type of the call.</param>
    /// <param name="entryAddress">The address of the entry point.</param>
    /// <param name="expectedReturnAddress">The expected address of return.</param>
    /// <param name="call">Instruction that initiated the call.</param>
    public void Call(CallType callType, SegmentedAddress entryAddress, SegmentedAddress? expectedReturnAddress, CfgInstruction? call) {
        Call(callType, entryAddress, expectedReturnAddress, call, null);
    }

    /// <summary>
    /// Calls a function
    /// </summary>
    /// <param name="callType">The type of the call.</param>
    /// <param name="entryAddress">The address of the entry point.</param>
    /// <param name="expectedReturnAddress">The expected address of return.</param>
    /// <param name="call">Instruction that initiated the call.</param>
    /// <param name="name">Name to give to the function.</param>
    public void Call(CallType callType, SegmentedAddress entryAddress, SegmentedAddress? expectedReturnAddress, CfgInstruction? call, string? name) {
        FunctionInformation currentFunction = _functionCatalogue.GetOrCreateFunctionInformation(entryAddress, name);
        if (UseCodeOverride && currentFunction.FunctionOverride is { } functionOverride) {
            OverrideUntilControlReturnsToInterpreter(currentFunction, functionOverride, expectedReturnAddress);
            // No need to register the function call, no associated ret was executed.
            return;
        }
        FunctionInformation? callerInfo = _functionCatalogue.GetFunctionInformation(CurrentFunctionCall);
        FunctionCall currentFunctionCall = new(callType, entryAddress, expectedReturnAddress, CurrentStackAddress, call);
        _callerStack.Push(currentFunctionCall);
        if (_callerStack.Count > MaxCallStackDepth) {
            _loggerService.LogWarning(
                "CALL STACK DEPTH EXCEEDED {MaxDepth}! Something is very wrong with the emulated code. Function tracking will be reset. Further returns may be flagged as misaligned.",
                MaxCallStackDepth);
            _callerStack.Clear();
        }
        if (_loggerService.IsEnabled(LogLevel.Trace)) {
            _loggerService.LogTrace("{Depth} Calling {CurrentFunction} from {Caller}", _callerStack.Count, currentFunction, callerInfo);
        }

        currentFunction.Enter(callerInfo);
    }

    /// <summary>
    /// Returns a string representation of the call stack.
    /// </summary>
    /// <returns>A string representation of the call stack.</returns>
    public string DumpCallStack() {
        StringBuilder res = new();
        foreach (FunctionCall functionCall in _callerStack.FromTopToBottom) {
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
        return PeekReturnAddressOnMachineStack(returnCallType, _state.StackPhysicalAddress);
    }

    /// <summary>
    /// Returns the return address of the specified call type from the machine stack at the specified physical address without removing it.
    /// </summary>
    /// <param name="returnCallType">The calling convention.</param>
    /// <param name="stackPhysicalAddress">The physical address of the stack.</param>
    /// <returns>The return address of the specified call type from the machine stack at the specified physical address without removing it.</returns>
    public SegmentedAddress? PeekReturnAddressOnMachineStack(CallType returnCallType, uint stackPhysicalAddress) {
        IMemory memory = _memory;
        // for NEAR32 and FAR32, return address is 4 bytes but in real mode we only care about the first 2
        return returnCallType switch {
            CallType.NEAR16 => new SegmentedAddress(_state.CS, memory.UInt16[stackPhysicalAddress]),
            CallType.NEAR32 => new SegmentedAddress(_state.CS, (ushort)memory.UInt32[stackPhysicalAddress]),
            CallType.FAR16 or CallType.INTERRUPT or CallType.EXTERNAL_INTERRUPT => memory.SegmentedAddress16[stackPhysicalAddress],
            CallType.FAR32 => memory.SegmentedAddress32[stackPhysicalAddress].ToSegmentedAddress(),
            CallType.MACHINE => null,
            _ => throw new ArgumentOutOfRangeException(nameof(returnCallType), returnCallType, "Unknown CallType")
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
    public void Ret(CallType returnCallType, CfgInstruction? ret) {
        FunctionCall? currentFunctionCallOrNull = CurrentFunctionCall;
        if (currentFunctionCallOrNull == null) {
            if (_loggerService.IsEnabled(LogLevel.Warning)) {
                _loggerService.LogWarning("Returning but no call was done before!!. Instruction is {Instruction}", ret);
            }
            return;
        }
        FunctionCall currentFunctionCall = currentFunctionCallOrNull.Value;
        if (returnCallType == CallType.MACHINE) {
            _callerStack.TryPop(out currentFunctionCall);
            if (currentFunctionCall.CallType != returnCallType) {
                _loggerService.LogWarning("Exiting machine entry point but current function does not seem to be entry point.");
            }
            return;
        }

        SegmentedAddress? actualReturnAddress = PeekReturnAddressOnMachineStack(returnCallType);
        FunctionCallStackMatch? returnMatch = _callerStack.FindReturnMatch(actualReturnAddress);
        if (returnMatch == null) {
            if (ret is not null && ret.IsReturn) {
                ret.CurrentCorrespondingCallInstruction = currentFunctionCall.Initiator;
            }
            HandleReturn(returnCallType, currentFunctionCall, false, actualReturnAddress);
            LogVerboseReturn(returnCallType, currentFunctionCall);
            return;
        }

        FunctionCallStackMatch nonNullReturnMatch = returnMatch.Value;
        bool returnAddressAlignedWithCallStack = nonNullReturnMatch.IsTop;
        FunctionCall functionCallToRecord = returnAddressAlignedWithCallStack
            ? nonNullReturnMatch.FunctionCall
            : currentFunctionCall;
        if (ret is not null && ret.IsReturn) {
            // Register the call instruction that probably caused this return
            ret.CurrentCorrespondingCallInstruction = functionCallToRecord.Initiator;
        }

        HandleReturn(returnCallType, functionCallToRecord, returnAddressAlignedWithCallStack, actualReturnAddress);
        _callerStack.PopThrough(nonNullReturnMatch);
        LogVerboseReturn(returnCallType, functionCallToRecord);
    }

    private void LogVerboseReturn(CallType returnCallType, FunctionCall currentFunctionCall) {
        if (_loggerService.IsEnabled(LogLevel.Trace)) {
            FunctionInformation? currentFunctionInformation = _functionCatalogue.GetFunctionInformation(currentFunctionCall);
            _loggerService.LogTrace("Returning from function {From} to function {To} ({TargetAddress})",
                currentFunctionInformation,
                _functionCatalogue.GetFunctionInformation(CurrentFunctionCall),
                PeekReturnAddressOnMachineStack(returnCallType));
        }
    }

    /// <summary>
    /// Runs the entered override and every override its control transfer lands on, then hands control back
    /// to the interpreter. A permissive override, which is every override registered by hand, ends its
    /// activation unconditionally: the interpreter continues at the CS:IP its action left, including the
    /// override's own address. A strict override, which generated code registers through
    /// <see cref="FunctionInformation.ForbidsInterpreterFallback"/>, owns the complete control flow of its
    /// code, so <see cref="RunOverrideChain"/> keeps dispatching until control reaches another override or
    /// the call's expected return address. A stopped machine ends the dispatch, and a non-matching unwind,
    /// or an unwind with no expected return address, propagates.
    /// </summary>
    /// <param name="initialFunction">The override the call itself entered.</param>
    /// <param name="initialOverride">The code of the override the call itself entered.</param>
    /// <param name="expectedReturnAddress">Return address of the call, or null when the call defines none.</param>
    /// <exception cref="CallStackUnwindException">A strict override transferred control to an address with
    /// no override that is not the call's expected return address, so continuing would fall back to the
    /// interpreter.</exception>
    private void OverrideUntilControlReturnsToInterpreter(FunctionInformation initialFunction,
        Func<int, Action> initialOverride, SegmentedAddress? expectedReturnAddress) {
        InterpreterChainCaller caller = new(expectedReturnAddress);
        if (caller.RunOverride(initialFunction, initialOverride)) {
            return;
        }
        RunOverrideChain(caller, initialFunction);
    }

    /// <summary>
    /// Runs the overrides that control transfers land on until control reaches the call's expected return
    /// address. Landing on an address that has no override and is not the expected return throws, because
    /// continuing would let the interpreter run code that the override chain owns.
    /// </summary>
    /// <param name="caller">The code waiting for the chain.</param>
    /// <param name="lastRunOverride">The override whose activation started the chain, or null when the
    /// caller's own C# code did.</param>
    /// <exception cref="CallStackUnwindException">Control landed on an address with no override that is not
    /// the expected return address.</exception>
    internal void RunOverrideChain(IOverrideChainCaller caller, FunctionInformation? lastRunOverride) {
        while (true) {
            if (!_state.IsRunning) {
                caller.OnMachineStopped();
                return;
            }
            SegmentedAddress landedAddress = _state.IpSegmentedAddress;
            bool atExpectedReturn = caller.ExpectedReturnAddress is { } expectedReturn && landedAddress == expectedReturn;
            if (atExpectedReturn && caller.ContinuesAtExpectedReturn) {
                return;
            }
            if (FindOverride(landedAddress) is { FunctionOverride: { } nextOverride } next) {
                if (caller.RunOverride(next, nextOverride)) {
                    return;
                }
                lastRunOverride = next;
                continue;
            }
            if (atExpectedReturn) {
                return;
            }
            string origin = lastRunOverride is null
                ? $"The call expecting to return to {caller.ExpectedReturnAddress}"
                : $"Override of {lastRunOverride}";
            throw new CallStackUnwindException(landedAddress,
                $"{origin} transferred control to {landedAddress} but there is no C# override at {landedAddress} " +
                "and it is not the call's expected return address. Refusing to fall back to the interpreter.");
        }
    }

    private FunctionInformation? FindOverride(SegmentedAddress address) {
        if (_functionCatalogue.FunctionInformations.TryGetValue(address, out FunctionInformation? function) &&
            function.HasOverride) {
            return function;
        }

        return null;
    }

    /// <summary>
    /// Gets or sets whether we call the C# override or the original machine code.
    /// </summary>
    public bool UseCodeOverride { get; set; }

    private void HandleReturn(CallType returnCallType, FunctionCall currentFunctionCall, bool returnAddressAlignedWithCallStack, SegmentedAddress? actualReturnAddress) {
        FunctionInformation? currentFunctionInformation = _functionCatalogue.GetFunctionInformation(currentFunctionCall);
        // Do not register functions when they are overriden
        if (currentFunctionInformation != null && !UseOverride(currentFunctionInformation)) {
            FunctionReturn currentFunctionReturn = new FunctionReturn(returnCallType, _state.IpSegmentedAddress);
            SegmentedAddress? addressToRecord = actualReturnAddress;

            if (returnAddressAlignedWithCallStack) {
                currentFunctionInformation.AddReturn(currentFunctionReturn, addressToRecord);
            } else {
                if (actualReturnAddress != null) {
                    LogUnalignedReturn(currentFunctionCall, currentFunctionInformation, returnCallType,
                        currentFunctionCall.ExpectedReturnAddress, actualReturnAddress.Value);
                }
                currentFunctionInformation.AddUnalignedReturn(currentFunctionReturn, addressToRecord);
            }
        }
    }

    private FunctionCall? CurrentFunctionCall {
        get => _callerStack.Current;
    }


    private SegmentedAddress CurrentStackAddress => new(_state.SS, _state.SP);

    private void LogUnalignedReturn(FunctionCall currentFunctionCall, FunctionInformation currentFunctionInformation,
        CallType returnCallType, SegmentedAddress? expectedReturnAddress,
        SegmentedAddress actualReturnAddress) {
        if (!_loggerService.IsEnabled(LogLevel.Warning)) {
            return;
        }

        FunctionReturn currentFunctionReturn = new FunctionReturn(returnCallType, _state.IpSegmentedAddress);
        if (currentFunctionInformation.UnalignedReturns.ContainsKey(currentFunctionReturn)) {
            return;
        }

        CallType callType = currentFunctionCall.CallType;
        SegmentedAddress stackAddressAfterCall = currentFunctionCall.StackAddressAfterCall;
        SegmentedAddress? returnAddressOnCallTimeStack =
            PeekReturnAddressOnMachineStack(callType, stackAddressAfterCall.Linear);
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

        _loggerService.LogWarning("""
                               PROGRAM IS NOT WELL BEHAVED SO CALL STACK COULD NOT BE TRACEABLE ANYMORE!
                               Current function {CurrentFunctionInformation} return instruction {CurrentFunctionReturn} will not go to the expected place:
                               - Expected return at {CallType} call time was {ExpectedReturnAddress} but return will go to {ActualReturnAddress}
                               - Return was stored on stack at address {StackAddressAfterCall} which contains {ReturnAddressOnCallTimeStack}
                               - Stack is now at address {CurrentStackAddress}
                               {AdditionalInformation}          
                               """,
            currentFunctionInformation.ToString(), currentFunctionReturn.ToString(),
            callType.ToString(), expectedReturnAddress?.ToString(), actualReturnAddress.ToString(),
            stackAddressAfterCall.ToString(), returnAddressOnCallTimeStack?.ToString(),
            currentStackAddress.ToString(),
            additionalInformation);
    }

    private bool UseOverride(FunctionInformation? functionInformation) {
        return UseCodeOverride && functionInformation != null && functionInformation.HasOverride;
    }

    /// <summary>The interpreter, which executed the call instruction and owns the continuation at the
    /// call's expected return address.</summary>
    private sealed class InterpreterChainCaller : IOverrideChainCaller {
        private readonly SegmentedAddress? _expectedReturnAddress;

        /// <summary>Creates the caller for a call that expects to return to <paramref name="expectedReturnAddress"/>.</summary>
        /// <param name="expectedReturnAddress">Return address of the call, or null when the call defines none.</param>
        public InterpreterChainCaller(SegmentedAddress? expectedReturnAddress) {
            _expectedReturnAddress = expectedReturnAddress;
        }

        /// <inheritdoc />
        public SegmentedAddress? ExpectedReturnAddress => _expectedReturnAddress;

        /// <inheritdoc />
        public bool ContinuesAtExpectedReturn => false;

        /// <inheritdoc />
        public bool RunOverride(FunctionInformation function, Func<int, Action> functionOverride) {
            try {
                Action? returnAction = functionOverride.Invoke(0);
                returnAction?.Invoke();
            } catch (CallStackUnwindException exception) when (_expectedReturnAddress is not null && exception.Target == _expectedReturnAddress.Value) {
                // An override that unwound the call stack back to the address this call was expected to
                // return to has completed the call: the interpreter continues at the unwind target.
                return true;
            }

            // A permissive override hands control back to the interpreter, a strict one keeps the chain.
            return !function.ForbidsInterpreterFallback;
        }

        /// <inheritdoc />
        public void OnMachineStopped() {
        }
    }
}