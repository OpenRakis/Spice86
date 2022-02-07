namespace Spice86.Emulator.Function;

using Serilog;

using Spice86.Emulator.CPU;
using Spice86.Emulator.VM;
using Spice86.Emulator.Memory;
using Spice86.Utils;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

public class FunctionHandler {
    private static readonly ILogger _logger = Log.Logger.ForContext<FunctionHandler>();

    private readonly Queue<FunctionCall> _callerStack = new();

    private readonly bool _debugMode;

    private readonly Machine _machine;

    private Dictionary<SegmentedAddress, FunctionInformation> _functionInformations = new();

    private bool _useCodeOverride;

    public FunctionHandler(Machine machine, bool debugMode) {
        this._machine = machine;
        this._debugMode = debugMode;
    }

    public void Call(CallType callType, ushort entrySegment, ushort entryOffset, ushort expectedReturnSegment, ushort expectedReturnOffset) {
        Call(callType, entrySegment, entryOffset, expectedReturnSegment, expectedReturnOffset, null, true);
    }

    public void Call(CallType callType, ushort entrySegment, ushort entryOffset, ushort? expectedReturnSegment, ushort? expectedReturnOffset, Func<String>? nameGenerator, bool recordReturn) {
        SegmentedAddress entryAddress = new(entrySegment, entryOffset);
        FunctionInformation currentFunction = getOrCreateFunctionInformation(entryAddress, nameGenerator);
        if (_debugMode) {
            FunctionInformation? caller = GetFunctionInformation(GetCurrentFunctionCall());
            SegmentedAddress? expectedReturnAddress = null;
            if (expectedReturnSegment != null && expectedReturnOffset != null) {
                expectedReturnAddress = new SegmentedAddress(expectedReturnSegment.Value, expectedReturnOffset.Value);
            }

            FunctionCall currentFunctionCall = new(callType, entryAddress, expectedReturnAddress, GetCurrentStackAddress(), recordReturn);
            _callerStack.Enqueue(currentFunctionCall);
            if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Debug)) {
                _logger.Debug("Calling {@CurrentFunction} from {@Caller}", currentFunction, caller);
            }

            currentFunction.Enter(caller);
        }

        if (_useCodeOverride) {
            currentFunction.CallOverride();
        }
    }

    private FunctionInformation getOrCreateFunctionInformation(SegmentedAddress entryAddress, Func<String>? nameGenerator) {
        FunctionInformation? res;
        _functionInformations.TryGetValue(entryAddress, out res);
        if (res is null) {
            res = new FunctionInformation(entryAddress, nameGenerator == null ? "unknown" : nameGenerator.Invoke());
            _functionInformations.Add(entryAddress, res);
        }
        return res;
    }

    public string DumpCallStack() {
        StringBuilder res = new();
        foreach (FunctionCall functionCall in this._callerStack) {
            SegmentedAddress? returnAddress = functionCall.GetExpectedReturnAddress();
            FunctionInformation? functionInformation = GetFunctionInformation(functionCall);
            res.Append(" - ");
            res.Append(functionInformation);
            res.Append(" expected to return to address ");
            res.Append(returnAddress);
            res.Append('\n');
        }

        return res.ToString();
    }

    public Dictionary<SegmentedAddress, FunctionInformation> GetFunctionInformations() {
        return _functionInformations;
    }

    public void Icall(CallType callType, ushort entrySegment, ushort entryOffset, ushort expectedReturnSegment, ushort expectedReturnOffset, byte vectorNumber, bool recordReturn) {
        Call(callType, entrySegment, entryOffset, expectedReturnSegment, expectedReturnOffset, () => $"interrupt_handler_{ConvertUtils.ToHex(vectorNumber)}", recordReturn);
    }

    public SegmentedAddress? PeekReturnAddressOnMachineStack(CallType returnCallType) {
        uint stackPhysicalAddress = GetStackPhysicalAddress();
        return PeekReturnAddressOnMachineStack(returnCallType, stackPhysicalAddress);
    }

    public SegmentedAddress? PeekReturnAddressOnMachineStack(CallType returnCallType, uint stackPhysicalAddress) {
        Memory memory = _machine.GetMemory();
        State state = _machine.GetCpu().GetState();
        return returnCallType switch {
            CallType.NEAR => new SegmentedAddress(state.GetCS(), memory.GetUint16(stackPhysicalAddress)),
            CallType.FAR or CallType.INTERRUPT => new SegmentedAddress(
                memory.GetUint16(stackPhysicalAddress + 2),
                memory.GetUint16(stackPhysicalAddress)),
            CallType.MACHINE => null,
            _ => null
        };
    }

    public SegmentedAddress? PeekReturnAddressOnMachineStackForCurrentFunction() {
        FunctionCall? currentFunctionCall = GetCurrentFunctionCall();
        if (currentFunctionCall == null) {
            return null;
        }

        return PeekReturnAddressOnMachineStack(currentFunctionCall.GetCallType());
    }

    public bool Ret(CallType returnCallType) {
        if (_debugMode) {
            if (_callerStack.TryDequeue(out FunctionCall? currentFunctionCall) == false) {
                if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
                    _logger.Warning("Returning but no call was done before!!");
                }
                return false;
            }
            FunctionInformation? currentFunctionInformation = GetFunctionInformation(currentFunctionCall);
            bool returnAddressAlignedWithCallStack = AddReturn(returnCallType, currentFunctionCall, currentFunctionInformation);
            if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Debug)) {
                _logger.Debug("Returning from {@CurrentFunctionInformation} to {@CurrentFunctionCall}", currentFunctionInformation, GetFunctionInformation(GetCurrentFunctionCall()));
            }

            if (!returnAddressAlignedWithCallStack) {
                _callerStack.Enqueue(currentFunctionCall);
            }
        }

        return true;
    }

    public void SetFunctionInformations(Dictionary<SegmentedAddress, FunctionInformation> functionInformations) {
        this._functionInformations = functionInformations;
    }

    public void SetUseCodeOverride(bool useCodeOverride) {
        this._useCodeOverride = useCodeOverride;
    }

    private bool AddReturn(CallType returnCallType, FunctionCall currentFunctionCall, FunctionInformation? currentFunctionInformation) {
        FunctionReturn currentFunctionReturn = GenerateCurrentFunctionReturn(returnCallType);
        SegmentedAddress? actualReturnAddress = PeekReturnAddressOnMachineStack(returnCallType);
        bool returnAddressAlignedWithCallStack = IsReturnAddressAlignedWithCallStack(currentFunctionCall, actualReturnAddress, currentFunctionReturn);
        if (currentFunctionInformation != null && !UseOverride(currentFunctionInformation)) {
            SegmentedAddress? addressToRecord = actualReturnAddress;
            if (!currentFunctionCall.IsRecordReturn()) {
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
        Cpu cpu = _machine.GetCpu();
        State state = cpu.GetState();
        ushort cs = state.GetCS();
        ushort ip = state.GetIP();
        return new FunctionReturn(returnCallType, new SegmentedAddress(cs, ip));
    }

    private FunctionCall? GetCurrentFunctionCall() {
        if (_callerStack.Any() == false) {
            return null;
        }
        return _callerStack.TryPeek(out FunctionCall? firstElement) ? firstElement : null;
    }

    private SegmentedAddress GetCurrentStackAddress() {
        State state = _machine.GetCpu().GetState();
        return new SegmentedAddress(state.GetSS(), state.GetSP());
    }

    private FunctionInformation? GetFunctionInformation(FunctionCall? functionCall) {
        if (functionCall == null) {
            return null;
        }
        if (_functionInformations.TryGetValue(functionCall.GetEntryPointAddress(), out FunctionInformation? value)) {
            return value;
        }
        return null;
    }

    private uint GetStackPhysicalAddress() {
        return _machine.GetCpu().GetState().GetStackPhysicalAddress();
    }

    private bool IsReturnAddressAlignedWithCallStack(FunctionCall currentFunctionCall, SegmentedAddress? actualReturnAddress, FunctionReturn currentFunctionReturn) {
        SegmentedAddress? expectedReturnAddress = currentFunctionCall.GetExpectedReturnAddress();

        // Null check necessary for machine stop call, in this case it won't be equals to what is in
        // the stack but it's expected.
        if (actualReturnAddress != null && !actualReturnAddress.Equals(expectedReturnAddress)) {
            FunctionInformation? currentFunctionInformation = GetFunctionInformation(currentFunctionCall);
            if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information) && currentFunctionInformation != null
                && !currentFunctionInformation.GetUnalignedReturns().ContainsKey(currentFunctionReturn)) {
                CallType callType = currentFunctionCall.GetCallType();
                SegmentedAddress stackAddressAfterCall = currentFunctionCall.GetStackAddressAfterCall();
                SegmentedAddress? returnAddressOnCallTimeStack = PeekReturnAddressOnMachineStack(callType, stackAddressAfterCall.ToPhysical());
                SegmentedAddress currentStackAddress = GetCurrentStackAddress();
                string additionalInformation = Environment.NewLine;
                if (!currentStackAddress.Equals(stackAddressAfterCall)) {
                    int delta = (int)Math.Abs((long)currentStackAddress.ToPhysical() - (long)stackAddressAfterCall.ToPhysical());
                    additionalInformation +=
                        $"Stack is not pointing at the same address as it was at call time. Delta is {delta} bytes{Environment.NewLine}";
                }
                if (!Object.Equals(expectedReturnAddress, returnAddressOnCallTimeStack)) {
                    additionalInformation += "Return address on stack was modified";
                }
                _logger.Information(@"PROGRAM IS NOT WELL BEHAVED SO CALL STACK COULD NOT BE TRACEABLE ANYMORE!
                        Current function {@CurrentFunctionInformation} return {@CurrentFunctionReturn} will not go to the expected place:
                        - At {@CallType} call time, return was supposed to be {@ExpectedReturnAddress} stored at SS:SP {@StackAddressAfterCall}. Value there is now {@ReturnAddressOnCallTimeStack}
                        - On the stack it is now {@ActualReturnAddress} stored at SS:SP {@CurrentStackAddress}
                        {@AdditionalInformation}
                    ",
                    currentFunctionInformation.ToString(), currentFunctionReturn.ToString(),
                    callType.ToString(), expectedReturnAddress?.ToString(), stackAddressAfterCall.ToString(), returnAddressOnCallTimeStack?.ToString(),
                    actualReturnAddress.ToString(), currentStackAddress.ToString(),
                    additionalInformation.ToString());
            }
            return false;
        }
        return true;
    }

    private bool UseOverride(FunctionInformation? functionInformation) {
        return this._useCodeOverride && functionInformation != null && functionInformation.HasOverride();
    }
}