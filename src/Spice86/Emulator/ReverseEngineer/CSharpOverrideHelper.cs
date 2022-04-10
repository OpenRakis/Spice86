namespace Spice86.Emulator.ReverseEngineer;

using Function.Dump;

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
    private static readonly ILogger _logger = Program.Logger.ForContext<CSharpOverrideHelper>();

    protected Cpu Cpu => Machine.Cpu;

    protected Machine Machine { get; }

    protected Memory Memory => Machine.Memory;

    protected UInt16Indexer UInt16 => Memory.UInt16;

    protected UInt8Indexer UInt8 => Memory.UInt8;
    protected Stack Stack => Cpu.Stack;

    protected State State => Cpu.State;

    protected Alu Alu => Cpu.Alu;

    public ushort AX { get => State.AX; set => State.AX = value; }
    public byte AH { get => State.AH; set => State.AH = value; }
    public byte AL { get => State.AL; set => State.AL = value; }

    public ushort BX { get => State.BX; set => State.BX = value; }
    public byte BH { get => State.BH; set => State.BH = value; }
    public byte BL { get => State.BL; set => State.BL = value; }

    public ushort CX { get => State.CX; set => State.CX = value; }
    public byte CH { get => State.CH; set => State.CH = value; }
    public byte CL { get => State.CL; set => State.CL = value; }

    public ushort DX { get => State.DX; set => State.DX = value; }
    public byte DH { get => State.DH; set => State.DH = value; }
    public byte DL { get => State.DL; set => State.DL = value; }

    public ushort SP { get => State.SP; set => State.SP = value; }
    public ushort BP { get => State.BP; set => State.BP = value; }

    public ushort SI { get => State.SI; set => State.SI = value; }
    public ushort DI { get => State.DI; set => State.DI = value; }

    public ushort CS { get => State.CS; set => State.CS = value; }
    public ushort DS { get => State.DS; set => State.DS = value; }
    public ushort ES { get => State.ES; set => State.ES = value; }
    public ushort FS { get => State.FS; set => State.FS = value; }
    public ushort GS { get => State.GS; set => State.GS = value; }
    public ushort SS { get => State.SS; set => State.SS = value; }

    public ushort IP { get => State.IP; set => State.IP = value; }


    public bool AuxiliaryFlag { get => State.AuxiliaryFlag; set => State.AuxiliaryFlag = value; }
    public bool CarryFlag { get => State.CarryFlag; set => State.CarryFlag = value; }
    public bool DirectionFlag { get => State.DirectionFlag; set => State.DirectionFlag = value; }
    public bool InterruptFlag { get => State.InterruptFlag; set => State.InterruptFlag = value; }
    public bool OverflowFlag { get => State.OverflowFlag; set => State.OverflowFlag = value; }
    public bool ParityFlag { get => State.ParityFlag; set => State.ParityFlag = value; }
    public bool SignFlag { get => State.SignFlag; set => State.SignFlag = value; }
    public bool TrapFlag { get => State.TrapFlag; set => State.TrapFlag = value; }
    public bool ZeroFlag { get => State.ZeroFlag; set => State.ZeroFlag = value; }
    public ushort FlagRegister { get => State.Flags.FlagRegister; set => State.Flags.FlagRegister = value; }

    private readonly Dictionary<SegmentedAddress, FunctionInformation> _functionInformations;

    protected JumpDispatcher JumpDispatcher { get; }

    public CSharpOverrideHelper(Dictionary<SegmentedAddress, FunctionInformation> functionInformations,
        Machine machine) {
        this._functionInformations = functionInformations;
        this.Machine = machine;
        this.JumpDispatcher = new();
    }

    public void DefineFunction(ushort segment, ushort offset, string name) {
        SegmentedAddress address = new(segment, offset);
        GetFunctionAtAddress(true, address);
        FunctionInformation functionInformation = new(address, name, null);
        _functionInformations.Add(address, functionInformation);
    }

    public void DefineFunction(ushort segment, ushort offset, Func<int, Action> overrideFunc,
        bool failOnExisting = true, string? name = null) {
        SegmentedAddress address = new(segment, offset);
        FunctionInformation? existing = GetFunctionAtAddress(failOnExisting, address);
        if (existing != null && existing.HasOverride) {
            // Do not overwrite existing code with override
            return;
        }

        String functionName;
        if (name != null) {
            functionName = name;
        } else {
            String methodName = overrideFunc.Method.Name;
            FunctionInformation? parsedFunctionInformation = GhidraSymbolsDumper.NameToFunctionInformation(methodName);
            if (parsedFunctionInformation == null) {
                throw new UnrecoverableException("Cannot parse " + methodName +
                                                 " into a spice86 function name as format is not correct.");
            }

            functionName = parsedFunctionInformation.Name;
        }

        _functionInformations[address] = (new(address, functionName, overrideFunc));
    }

    private FunctionInformation? GetFunctionAtAddress(bool failOnExisting, SegmentedAddress address) {
        if (_functionInformations.TryGetValue(address, out FunctionInformation? existingFunctionInformation)) {
            if (!failOnExisting) {
                return existingFunctionInformation;
            }

            string error =
                $"There is already a function overriden at address {address} named {existingFunctionInformation.Name}. Please check your mappings for duplicates.";
            if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Error)) {
                _logger.Error(
                    "There is already a function defined at address {@Address} named {@ExistingFunctionInformationName} but you are trying to redefine it. Please check your mappings for duplicates.",
                    address, existingFunctionInformation.Name);
            }

            throw new UnrecoverableException(error);
        }

        return null;
    }

    public void DefineStaticAddress(ushort segment, ushort offset, string name) {
        DefineStaticAddress(segment, offset, name, false);
    }

    public void DefineStaticAddress(ushort segment, ushort offset, string name, bool whiteListOnlyThisSegment) {
        SegmentedAddress address = new(segment, offset);
        uint physicalAddress = address.ToPhysical();
        StaticAddressesRecorder recorder = Cpu.StaticAddressesRecorder;
        if (recorder.Names.TryGetValue(physicalAddress, out string? existing)) {
            string error =
                $"There is already a static address defined at address {address} named {existing} but you are trying to redefine it as {name}. Please check your mappings for duplicates.";
            if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Error)) {
                _logger.Error(
                    "There is already a static address defined at address {@Address} named {@Existing} but you are trying to redefine it. Please check your mappings for duplicates.",
                    address, existing);
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
            State.CS = cs;
            State.IP = ip;
        };
    }

    public Action FarRet(ushort numberOfBytesToPop = 0) {
        return () => Cpu.FarRet(numberOfBytesToPop);
    }

    public Action InterruptRet() {
        return () => Cpu.InterruptRet();
    }

    public Action NearJump(ushort ip) {
        return () => State.IP = ip;
    }

    public Action NearRet(ushort numberOfBytesToPop = 0) {
        return () => Cpu.NearRet(numberOfBytesToPop);
    }

    public void NearCall(ushort expectedReturnCs, ushort expectedReturnIp, Func<int, Action> function) {
        ExecuteEnsuringSameStack(expectedReturnCs, expectedReturnIp, () => {
            Stack.Push(expectedReturnIp);
            Action returnAction = function.Invoke(0);
            returnAction.Invoke();
        });
    }

    public void FarCall(ushort expectedReturnCs, ushort expectedReturnIp, Func<int, Action> function) {
        ExecuteEnsuringSameStack(expectedReturnCs, expectedReturnIp, () => {
            Stack.Push(expectedReturnCs);
            Stack.Push(expectedReturnIp);
            Action returnAction = function.Invoke(0);
            returnAction.Invoke();
        });
    }

    public void InterruptCall(ushort expectedReturnCs, ushort expectedReturnIp, Func<int, Action> function) {
        ExecuteEnsuringSameStack(expectedReturnCs, expectedReturnIp, () => {
            Stack.Push(FlagRegister);
            Stack.Push(expectedReturnCs);
            Stack.Push(expectedReturnIp);
            Action returnAction = function.Invoke(0);
            returnAction.Invoke();
        });
    }

    public void InterruptCall(ushort expectedReturnCs, ushort expectedReturnIp, int vectorNumber) {
        ushort targetIP = Memory.GetUint16((ushort)(4 * vectorNumber));
        ushort targetCS = Memory.GetUint16((ushort)(4 * vectorNumber + 2));
        SegmentedAddress target = new SegmentedAddress(targetCS, targetIP);
        Func<int, Action>? function = SearchFunctionOverride(target);
        if (function == null) {
            throw FailAsUntested($"Could not find an override at address {target}");
        }

        InterruptCall(expectedReturnCs, expectedReturnIp, function);
    }

    public Func<int, Action>? SearchFunctionOverride(SegmentedAddress target) {
        if (!Machine.Cpu.FunctionHandler.FunctionInformations.TryGetValue(target,
                out FunctionInformation? functionInformation)) {
            return null;
        }

        return functionInformation.FuntionOverride;
    }

    private void ExecuteEnsuringSameStack(ushort expectedReturnCs, ushort expectedReturnIp, Action action) {
        uint stackAddressBefore = State.StackPhysicalAddress;
        State.CS = expectedReturnCs;
        State.IP = expectedReturnIp;
        action.Invoke();
        ushort actualReturnCs = State.CS;
        ushort actualReturnIp = State.IP;
        uint stackAddressAfter = State.StackPhysicalAddress;
        if (actualReturnCs != expectedReturnCs || actualReturnIp != expectedReturnIp) {
            SegmentedAddress expectedReturn = new SegmentedAddress(expectedReturnCs, expectedReturnIp);
            SegmentedAddress actualReturn = new SegmentedAddress(actualReturnCs, actualReturnIp);
            String message =
                "The original code is trying to jump via call stack modification. Expected to return at: " +
                expectedReturn + " but actually returning to: " + actualReturn + " Stack address before: " +
                stackAddressBefore + " Stack address after: " + stackAddressAfter;
            if (!_functionInformations.TryGetValue(actualReturn, out FunctionInformation? actualTarget)) {
                throw this.FailAsUntested(message);
            }

            message += " Found " + actualTarget.Name + " there.";
            if (actualTarget.FuntionOverride != null) {
                message += " Calling it.";
                _logger.Warning(message);
                actualTarget.FuntionOverride.Invoke(0);
            } else {
                throw this.FailAsUntested(message);
            }
        }
    }

    public void OverrideInstruction(ushort segment, ushort offset, Func<Action> renamedOverride) {
        AddressBreakPoint breakPoint = new(
            BreakPointType.EXECUTION,
            MemoryUtils.ToPhysicalAddress(
                segment,
                offset),
            _ => renamedOverride.Invoke()
            , false);
        Machine.MachineBreakpoints.ToggleBreakPoint(breakPoint, true);
    }

    public void SetProvidedInterruptHandlersAsOverridden() {
        CallbackHandler callbackHandler = Machine.CallbackHandler;
        foreach (KeyValuePair<byte, SegmentedAddress> callbackAddressEntry in callbackHandler.GetCallbackAddresses()) {
            byte callbackNumber = callbackAddressEntry.Key;
            SegmentedAddress callbackAddress = callbackAddressEntry.Value;
            var runnable = new Func<int, Action>(_ => {
                callbackHandler.Run(callbackNumber);
                return InterruptRet();
            });
            DefineFunction(callbackAddress.Segment, callbackAddress.Offset, runnable, false,
                $"provided_interrupt_handler_{ConvertUtils.ToHex(callbackNumber)}");
        }
    }

    protected void CheckVtableContainsExpected(int segmentRegisterIndex, ushort offset, ushort expectedSegment,
        ushort expectedOffset) {
        uint address = MemoryUtils.ToPhysicalAddress(State.SegmentRegisters.GetRegister(segmentRegisterIndex), offset);
        ushort foundOffset = Memory.GetUint16(address);
        ushort foundSegment = Memory.GetUint16(address + 2);
        if (foundOffset != expectedOffset || foundSegment != expectedSegment) {
            throw this.FailAsUntested(
                $"Call table value changed, we would not call the method the game is calling. Expected: {new SegmentedAddress(expectedSegment, expectedOffset)} found: {new SegmentedAddress(foundSegment, foundOffset)}");
        }
    }

    protected void DefineExecutableArea(uint startAddress, uint endAddress) {
        for (uint address = startAddress; address <= endAddress; address++) {
            // For closure
            uint addressCopy = address;
            AddressBreakPoint breakPoint = new AddressBreakPoint(BreakPointType.WRITE, address, _ => {
                Machine.Cpu.ExecutionFlowRecorder.RegisterExecutableCodeModification(
                    new SegmentedAddress(State.CS, State.IP), addressCopy);
            }, false);
            Machine.MachineBreakpoints.ToggleBreakPoint(breakPoint, true);
        }
    }

    /// <summary>
    /// Call this in your override when you re-implement a function with a branch that seems never
    /// reached.
    /// </summary>
    protected UnrecoverableException FailAsUntested(string message) {
        string dumpedCallStack = Machine.DumpCallStack();
        string error =
            $"Untested code reached, please tell us how to reach this state.Here is the message: {message} Here is the call stack: {dumpedCallStack}";
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Error)) {
            _logger.Error(
                "Untested code reached, please tell us how to reach this state.Here is the message: {@Message} Here is the call stack: {@DumpedCallStack}",
                message, Machine.DumpCallStack());
        }

        return new UnrecoverableException(error);
    }

    protected void Interrupt(int vectorNumber) {
        Machine.CallbackHandler.RunFromOverriden(vectorNumber);
    }

    protected Action Hlt() {
        return () => {
            _logger.Information("Program requested exit. Terminating now.");
            Environment.Exit(0);
        };
    }
}