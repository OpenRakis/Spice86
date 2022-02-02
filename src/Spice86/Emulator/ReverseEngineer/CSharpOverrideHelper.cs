namespace Spice86.Emulator.ReverseEngineer;

using Serilog;

using Spice86.Emulator.Callback;
using Spice86.Emulator.CPU;
using Spice86.Emulator.Errors;
using Spice86.Emulator.Function;
using Spice86.Emulator.VM;
using Spice86.Emulator.VM.Breakpoint;
using Spice86.Emulator.Memory;
using Spice86.Utils;

using System;
using System.Collections.Generic;

public class CSharpOverrideHelper {
    private static readonly ILogger _logger = Log.Logger.ForContext<CSharpOverrideHelper>();

    protected readonly Cpu _cpu;

    protected readonly Machine _machine;

    protected readonly Memory _memory;

    protected readonly Stack _stack;

    protected readonly State _state;
    
    private readonly string _prefix;

    private readonly Dictionary<SegmentedAddress, FunctionInformation> functionInformations;

    public CSharpOverrideHelper(Dictionary<SegmentedAddress, FunctionInformation> functionInformations, string prefix, Machine machine) {
        this.functionInformations = functionInformations;
        this._prefix = prefix;
        this._machine = machine;
        this._cpu = machine.GetCpu();
        this._memory = machine.GetMemory();
        this._state = _cpu.GetState();
        this._stack = _cpu.GetStack();
    }

    public void DefineFunction(ushort segment, ushort offset, string suffix) {
        this.DefineFunction(segment, offset, suffix, null);
    }

    public void DefineFunction(ushort segment, ushort offset, string suffix, Func<Action>? @override) {
        SegmentedAddress address = new(segment, offset);
        string name = $"{_prefix}.{suffix}";
        if (functionInformations.TryGetValue(address, out var existingFunctionInformation)) {
            string error = $"There is already a function defined at address {address} named {existingFunctionInformation.GetName()} but you are trying to redefine it as {name}. Please check your mappings for duplicates.";
            if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Error)) {
                _logger.Error("There is already a function defined at address {@Address} named {@ExistingFunctionInformationName} but you are trying to redefine it as {@Name}. Please check your mappings for duplicates.", address, existingFunctionInformation.GetName(), name);
            }
            throw new UnrecoverableException(error);
        }
        FunctionInformation functionInformation = new(address, name, @override);
        functionInformations.Add(address, functionInformation);
    }

    public void DefineStaticAddress(ushort segment, ushort offset, string name) {
        DefineStaticAddress(segment, offset, name, false);
    }

    public void DefineStaticAddress(ushort segment, ushort offset, string name, bool whiteListOnlyThisSegment) {
        SegmentedAddress address = new(segment, offset);
        uint physicalAddress = address.ToPhysical();
        StaticAddressesRecorder recorder = _cpu.GetStaticAddressesRecorder();
        if (recorder.GetNames().TryGetValue(physicalAddress, out var existing)) {
            string error = $"There is already a static address defined at address {address} named {existing} but you are trying to redefine it as {name}. Please check your mappings for duplicates.";
            if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Error)) {
                _logger.Error("There is already a static address defined at address {@Address} named {@Existing} but you are trying to redefine it as {@Name}. Please check your mappings for duplicates.", address, existing, name);
            }
            throw new UnrecoverableException(error);
        }

        recorder.AddName(physicalAddress, name);
        if (whiteListOnlyThisSegment) {
            recorder.AddSegmentTowhiteList(address);
        }
    }

    public Action FarJump(ushort cs, ushort ip) {
        return () => {
            _state.SetCS(cs);
            _state.SetIP(ip);
        };
    }

    public Action FarRet() {
        return () => _cpu.FarRet(0);
    }

    public Action InterruptRet() {
        return () => _cpu.InterruptRet();
    }

    public Action NearJump(ushort ip) {
        return () => _state.SetIP(ip);
    }

    public Action NearRet() {
        return () => _cpu.NearRet(0);
    }

    public void OverrideInstruction(ushort segment, ushort offset, Func<Action> renamedOverride) {
        BreakPoint breakPoint = new(
            BreakPointType.EXECUTION,
            MemoryUtils.ToPhysicalAddress(
                segment,
                offset),
            (b) => renamedOverride.Invoke()
        , false);
        _machine.GetMachineBreakpoints().ToggleBreakPoint(breakPoint, true);
    }

    public void SetProvidedInterruptHandlersAsOverridden() {
        CallbackHandler callbackHandler = _machine.GetCallbackHandler();
        Dictionary<byte, SegmentedAddress> callbackAddresses = callbackHandler.GetCallbackAddresses();
        foreach (KeyValuePair<byte, SegmentedAddress> callbackAddressEntry in callbackAddresses) {
            byte callbackNumber = callbackAddressEntry.Key;
            SegmentedAddress callbackAddress = callbackAddressEntry.Value;
            DefineFunction(callbackAddress.GetSegment(), callbackAddress.GetOffset(), $"provided_interrupt_handler_{ConvertUtils.ToHex(callbackNumber)}",
            new Func<Action>(() => {
                callbackHandler.Run(callbackNumber);
                return InterruptRet();
            }));
        }
    }

    protected void CheckVtableContainsExpected(int segmentRegisterIndex, ushort offset, ushort expectedSegment, ushort expectedOffset) {
        uint address = MemoryUtils.ToPhysicalAddress(_state.GetSegmentRegisters().GetRegister(segmentRegisterIndex), offset);
        ushort foundOffset = _memory.GetUint16(address);
        ushort foundSegment = _memory.GetUint16(address + 2);
        if (foundOffset != expectedOffset || foundSegment != expectedSegment) {
            this.FailAsUntested($"Call table value changed, we would not call the method the game is calling. Expected: {new SegmentedAddress(expectedSegment, expectedOffset)} found: {new SegmentedAddress(foundSegment, foundOffset)}");
        }
    }

    /// <summary>
    /// Call this in your override when you re-implement a function with a branch that seems never
    /// reached. @param message
    /// </summary>
    protected void FailAsUntested(string message) {
        string dumpedCallStack = _machine.DumpCallStack();
        string error = $"Untested code reached, please tell us how to reach this state.Here is the message: {message} Here is the call stack: {dumpedCallStack}";
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Error)) {
            _logger.Error("Untested code reached, please tell us how to reach this state.Here is the message: {@Message} Here is the call stack: {@DumpedCallStack}", message, _machine.DumpCallStack());
        }
        throw new UnrecoverableException(error);
    }
}