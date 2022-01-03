namespace Spice86.Emulator.Function;

using Serilog;

using Spice86.Emulator.Cpu;
using Spice86.Emulator.Machine;
using Spice86.Emulator.Memory;
using Spice86.Utils;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

public class FunctionHandler
{
    private static readonly ILogger _logger = Log.Logger.ForContext<FunctionHandler>();
    private readonly Machine _machine;
    private readonly Queue<FunctionCall> _callerStack = new();
    private Dictionary<SegmentedAddress, FunctionInformation> _functionInformations = new();
    private bool _useCodeOverride;
    private readonly bool _debugMode;
    public FunctionHandler(Machine machine, bool debugMode)
    {
        this._machine = machine;
        this._debugMode = debugMode;
    }

    public virtual void SetFunctionInformations(Dictionary<SegmentedAddress, FunctionInformation> functionInformations)
    {
        this._functionInformations = functionInformations;
    }

    public virtual Dictionary<SegmentedAddress, FunctionInformation> GetFunctionInformations()
    {
        return _functionInformations;
    }

    public virtual void SetUseCodeOverride(bool useCodeOverride)
    {
        this._useCodeOverride = useCodeOverride;
    }

    public virtual void Icall(CallType callType, int entrySegment, int entryOffset, int expectedReturnSegment, int expectedReturnOffset, int vectorNumber, bool recordReturn)
    {
        Call(callType, entrySegment, entryOffset, expectedReturnSegment, expectedReturnOffset, () => $"interrupt_handler_{ConvertUtils.ToHex(vectorNumber)}", recordReturn);
    }

    public virtual void Call(CallType callType, int entrySegment, int entryOffset, int expectedReturnSegment, int expectedReturnOffset)
    {
        Call(callType, entrySegment, entryOffset, expectedReturnSegment, expectedReturnOffset, null, true);
    }

    public virtual void Call(CallType callType, int entrySegment, int entryOffset, int? expectedReturnSegment, int? expectedReturnOffset, Func<String>? nameGenerator, bool recordReturn)
    {
        SegmentedAddress entryAddress = new(entrySegment, entryOffset);
        FunctionInformation currentFunction = _functionInformations.GetValueOrDefault(entryAddress, new FunctionInformation(entryAddress, nameGenerator != null ? nameGenerator.Invoke() : "unknown"));
        if (_debugMode)
        {
            FunctionInformation? caller = GetFunctionInformation(GetCurrentFunctionCall());
            SegmentedAddress? expectedReturnAddress = null;
            if (expectedReturnSegment != null && expectedReturnOffset != null)
            {
                expectedReturnAddress = new SegmentedAddress(expectedReturnSegment.Value, expectedReturnOffset.Value);
            }

            FunctionCall currentFunctionCall = new(callType, entryAddress, expectedReturnAddress, GetCurrentStackAddress(), recordReturn);
            _callerStack.Enqueue(currentFunctionCall);
            if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Debug))
            {
                _logger.Debug("Calling {@CurrentFunction} from {@Caller}", currentFunction, caller);
            }

            currentFunction.Enter(caller);
        }

        if (_useCodeOverride)
        {
            currentFunction.CallOverride();
        }
    }

    public virtual bool Ret(CallType returnCallType)
    {
        if (_debugMode)
        {

            if (_callerStack.TryDequeue(out var currentFunctionCall) == false)
            {
                _logger.Warning("Returning but no call was done before!!");
                return false;
            }
            FunctionInformation? currentFunctionInformation = GetFunctionInformation(currentFunctionCall);
            bool returnAddressAlignedWithCallStack = AddReturn(returnCallType, currentFunctionCall, currentFunctionInformation);
            if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Debug))
            {
                _logger.Debug("Returning from {@CurrentFunctionInformation} to {@CurrentFunctionCall}", currentFunctionInformation, GetFunctionInformation(GetCurrentFunctionCall()));
            }

            if (!returnAddressAlignedWithCallStack)
            {
                _callerStack.Enqueue(currentFunctionCall);
            }
        }

        return true;
    }

    private bool AddReturn(CallType returnCallType, FunctionCall currentFunctionCall, FunctionInformation? currentFunctionInformation)
    {
        FunctionReturn currentFunctionReturn = GenerateCurrentFunctionReturn(returnCallType);
        SegmentedAddress actualReturnAddress = PeekReturnAddressOnMachineStack(returnCallType);
        bool returnAddressAlignedWithCallStack = IsReturnAddressAlignedWithCallStack(currentFunctionCall, actualReturnAddress, currentFunctionReturn);
        if (currentFunctionInformation != null && !UseOverride(currentFunctionInformation))
        {
            SegmentedAddress? addressToRecord = actualReturnAddress;
            if (!currentFunctionCall.IsRecordReturn())
            {
                addressToRecord = null;
            }

            if (returnAddressAlignedWithCallStack)
            {
                currentFunctionInformation.AddReturn(currentFunctionReturn, addressToRecord);
            }
            else
            {
                currentFunctionInformation.AddUnalignedReturn(currentFunctionReturn, addressToRecord);
            }
        }

        return returnAddressAlignedWithCallStack;
    }

    private bool IsReturnAddressAlignedWithCallStack(FunctionCall currentFunctionCall, SegmentedAddress actualReturnAddress, FunctionReturn currentFunctionReturn)
    {
        SegmentedAddress? expectedReturnAddress = currentFunctionCall.GetExpectedReturnAddress();
        // Null check necessary for machine stop call, in this case it won't be equals to what is in the stack but it's
        // expected.
        if (actualReturnAddress != null && !actualReturnAddress.Equals(expectedReturnAddress))
        {
            FunctionInformation? currentFunctionInformation = GetFunctionInformation(currentFunctionCall);
            if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information) && currentFunctionInformation != null
                && !currentFunctionInformation.GetUnalignedReturns().ContainsKey(currentFunctionReturn))
            {
                CallType callType = currentFunctionCall.GetCallType();
                SegmentedAddress stackAddressAfterCall = currentFunctionCall.GetStackAddressAfterCall();
                SegmentedAddress returnAddressOnCallTimeStack =
                    PeekReturnAddressOnMachineStack(callType, stackAddressAfterCall.ToPhysical());
                SegmentedAddress currentStackAddress = GetCurrentStackAddress();
                string additionalInformation = Environment.NewLine;
                if (!currentStackAddress.Equals(stackAddressAfterCall))
                {
                    int delta = Math.Abs(currentStackAddress.ToPhysical() - stackAddressAfterCall.ToPhysical());
                    additionalInformation +=
                        $"Stack is not pointing at the same address as it was at call time. Delta is {delta} bytes{Environment.NewLine}";
                }
                if (!Object.Equals(expectedReturnAddress, returnAddressOnCallTimeStack))
                {
                    additionalInformation += "Return address on stack was modified";
                }
                _logger.Information(@"PROGRAM IS NOT WELL BEHAVED SO CALL STACK COULD NOT BE TRACEABLE ANYMORE!
                        Current function {@CurrentFunctionInformation}
                        return {@CurrentFunctionReturn}
                        will not go to the expected place:
                        -At {@CallType}
                        call time, return was supposed to be {@ExpectedReturnAddress}
                        stored at SS: SP {@StackAddressAfterCall}. Value there is now {@ReturnAddressOnCallTimeStack}
                        - On the stack it is now {@ActualReturnAddress} stored at SS:SP {@CurrentStackAddress}
                        {@AdditionalInformation}
                    ",
                    currentFunctionInformation, currentFunctionReturn,
                    callType, expectedReturnAddress, stackAddressAfterCall, returnAddressOnCallTimeStack,
                    actualReturnAddress, currentStackAddress,
                    additionalInformation);
            }
            return false;
        }
        return true;
    }

    private int GetStackPhysicalAddress()
    {
        return _machine.GetCpu().GetState().GetStackPhysicalAddress();
    }

    private SegmentedAddress GetCurrentStackAddress()
    {
        State state = _machine.GetCpu().GetState();
        return new SegmentedAddress(state.GetSS(), state.GetSP());
    }

    public virtual SegmentedAddress PeekReturnAddressOnMachineStack(CallType returnCallType)
    {
        int stackPhysicalAddress = GetStackPhysicalAddress();
        return PeekReturnAddressOnMachineStack(returnCallType, stackPhysicalAddress);
    }

    public virtual SegmentedAddress PeekReturnAddressOnMachineStack(CallType returnCallType, int stackPhysicalAddress)
    {
        Memory memory = _machine.GetMemory();
        State state = _machine.GetCpu().GetState();
        return new SegmentedAddress(state.GetCS(), memory.GetUint16(stackPhysicalAddress));
    }

    public virtual SegmentedAddress? PeekReturnAddressOnMachineStackForCurrentFunction()
    {
        FunctionCall? currentFunctionCall = GetCurrentFunctionCall();
        if (currentFunctionCall == null)
        {
            return null;
        }

        return PeekReturnAddressOnMachineStack(currentFunctionCall.GetCallType());
    }

    private FunctionReturn GenerateCurrentFunctionReturn(CallType returnCallType)
    {
        Cpu cpu = _machine.GetCpu();
        State state = cpu.GetState();
        int cs = state.GetCS();
        int ip = state.GetIP();
        return new FunctionReturn(returnCallType, new SegmentedAddress(cs, ip));
    }

    private FunctionCall? GetCurrentFunctionCall()
    {
        if (_callerStack.Any() == false)
        {
            return null;
        }
        return _callerStack.TryPeek(out var firstElement) ? firstElement : null;
    }

    private bool UseOverride(FunctionInformation functionInformation)
    {
        return this._useCodeOverride && functionInformation != null && functionInformation.HasOverride();
    }

    private FunctionInformation? GetFunctionInformation(FunctionCall? functionCall)
    {
        if (functionCall == null)
        {
            return null;
        }
        if (_functionInformations.TryGetValue(functionCall.GetEntryPointAddress(), out var value))
        {
            return value;
        }
        return null;
    }

    public virtual string DumpCallStack()
    {
        StringBuilder res = new();
        foreach (FunctionCall functionCall in this._callerStack)
        {
            SegmentedAddress? returnAddress = functionCall.GetExpectedReturnAddress();
            FunctionInformation? functionInformation = GetFunctionInformation(functionCall);
            res.Append(" - ");
            res.Append(functionInformation);
            res.Append(" expected to return to address ");
            res.Append(returnAddress);
            res.Append('\\');
        }

        return res.ToString();
    }
}
