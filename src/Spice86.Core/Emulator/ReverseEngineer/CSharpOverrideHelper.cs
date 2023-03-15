using Serilog.Events;

using Spice86.Core.Emulator.Callback;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Function.Dump;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Core.Utils;
using Spice86.Shared.Interfaces;

using System.Linq;

namespace Spice86.Core.Emulator.ReverseEngineer;

public class CSharpOverrideHelper {
    protected readonly ILoggerService _loggerService;

    public Cpu Cpu => Machine.Cpu;

    public Machine Machine { get; }

    public Memory.Memory Memory => Machine.Memory;

    public UInt8Indexer UInt8 => Memory.UInt8;
    public UInt16Indexer UInt16 => Memory.UInt16;
    public UInt32Indexer UInt32 => Memory.UInt32;
    public Stack Stack => Cpu.Stack;

    public State State => Cpu.State;

    public Alu Alu => Cpu.Alu;

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
    public uint FlagRegister { get => State.Flags.FlagRegister; set => State.Flags.FlagRegister = value; }
    public ushort FlagRegister16 { get => State.Flags.FlagRegister16; set => State.Flags.FlagRegister = value; }

    public short Direction8 => State.Direction8;
    public short Direction16 => State.Direction16;
    public short Direction32 => State.Direction32;
    protected readonly Dictionary<SegmentedAddress, FunctionInformation> _functionInformations;

    public JumpDispatcher JumpDispatcher { get; set; }

    public bool IsRegisterExecutableCodeModificationEnabled {
        get => Machine.Cpu.ExecutionFlowRecorder.IsRegisterExecutableCodeModificationEnabled;
        set => Machine.Cpu.ExecutionFlowRecorder.IsRegisterExecutableCodeModificationEnabled = value;
    }

    public CSharpOverrideHelper(Dictionary<SegmentedAddress, FunctionInformation> functionInformations,
        Machine machine, ILoggerService loggerService) {
        _loggerService = loggerService;
        _functionInformations = functionInformations;
        Machine = machine;
        JumpDispatcher = new();
    }

    public void DefineFunction(ushort segment, ushort offset, string name) {
        SegmentedAddress address = new(segment, offset);
        GetFunctionAtAddress(true, address);
        FunctionInformation functionInformation = new(address, name, null);
        _functionInformations.Add(address, functionInformation);
    }

    public void DefineFunction(ushort segment,
        ushort offset,
        Func<int, Action> overrideFunc,
        bool failOnExisting = true,
        string? name = null) {
        SegmentedAddress address = new(segment, offset);
        FunctionInformation? existing = GetFunctionAtAddress(failOnExisting, address);
        if (existing?.HasOverride is true) {
            // Do not overwrite existing code with override
            return;
        }

        string functionName;
        if (name != null) {
            functionName = name;
        } else {
            string methodName = overrideFunc.Method.Name;
            FunctionInformation? parsedFunctionInformation = GhidraSymbolsDumper.NameToFunctionInformation(_loggerService, methodName);
            if (parsedFunctionInformation == null) {
                throw new UnrecoverableException("Cannot parse " + methodName +
                    " into a spice86 function name as format is not correct.");
            }

            functionName = parsedFunctionInformation.Name;
        }

        _functionInformations[address] = (new(address, functionName, overrideFunc));
    }

    public FunctionInformation? GetFunctionAtAddress(bool failOnExisting, SegmentedAddress address) {
        if (_functionInformations.TryGetValue(address, out FunctionInformation? existingFunctionInformation)) {
            if (!failOnExisting) {
                return existingFunctionInformation;
            }

            string error =
                $"There is already a function overriden at address {address} named {existingFunctionInformation.Name}. Please check your mappings for duplicates.";
            if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                _loggerService.Error(
                    "There is already a function defined at address {@Address} named {@ExistingFunctionInformationName} but you are trying to redefine it. Please check your mappings for duplicates.",
                    address, existingFunctionInformation.Name);
            }

            throw new UnrecoverableException(error);
        }

        return null;
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
        ExecuteCallEnsuringSameStack(expectedReturnCs, expectedReturnIp, function, () => {
            Stack.Push16(expectedReturnIp);
            Action returnAction = function.Invoke(0);
            returnAction.Invoke();
        });
    }

    public void FarCall(ushort expectedReturnCs, ushort expectedReturnIp, Func<int, Action> function) {
        ExecuteCallEnsuringSameStack(expectedReturnCs, expectedReturnIp, function, () => {
            Stack.Push16(expectedReturnCs);
            Stack.Push16(expectedReturnIp);
            Action returnAction = function.Invoke(0);
            returnAction.Invoke();
        });
    }

    public void InterruptCall(ushort expectedReturnCs, ushort expectedReturnIp, Func<int, Action> function) {
        ExecuteCallEnsuringSameStack(expectedReturnCs, expectedReturnIp, function, () => {
            Stack.Push16(State.Flags.FlagRegister16);
            InterruptFlag = false;
            Stack.Push16(expectedReturnCs);
            Stack.Push16(expectedReturnIp);
            Action returnAction = function.Invoke(0);
            returnAction.Invoke();
        });
    }

    public void InterruptCall(ushort expectedReturnCs, ushort expectedReturnIp, int vectorNumber) {
        ushort targetIP = Memory.GetUint16((ushort)(4 * vectorNumber));
        ushort targetCS = Memory.GetUint16((ushort)((4 * vectorNumber) + 2));
        SegmentedAddress target = new SegmentedAddress(targetCS, targetIP);
        Func<int, Action>? function = SearchFunctionOverride(target);
        if (function is null) {
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

    private void ExecuteCallEnsuringSameStack(ushort expectedReturnCs, ushort expectedReturnIp,
        Func<int, Action> function, Action action) {
        uint expectedStackAddress = State.StackPhysicalAddress;
        State.CS = expectedReturnCs;
        State.IP = expectedReturnIp;
        ExecuteCall(function, action);
        ushort actualReturnCs = State.CS;
        ushort actualReturnIp = State.IP;
        uint actualStackAddress = State.StackPhysicalAddress;
        // Do not return to the caller until we are sure we are at the right place
        while ( actualReturnCs != expectedReturnCs ||
                actualReturnIp != expectedReturnIp) {
            SegmentedAddress expectedReturn = new SegmentedAddress(expectedReturnCs, expectedReturnIp);
            SegmentedAddress actualReturn = new SegmentedAddress(actualReturnCs, actualReturnIp);
            string message =
                "The original code is trying to jump via call stack modification. Expected to return at: " +
                expectedReturn + " but actually returning to: " + actualReturn + " Stack address before: " +
                expectedStackAddress + " Stack address after: " + actualStackAddress;
            if (!_functionInformations.TryGetValue(actualReturn, out FunctionInformation? actualTarget)) {
                throw FailAsUntested(message);
            }

            message += " Found " + actualTarget.Name + " there.";
            if (actualTarget.FuntionOverride != null) {
                message += " Calling it.";
                _loggerService.Warning("{Message}", message);
                ExecuteCall(actualTarget.FuntionOverride, () => actualTarget.FuntionOverride.Invoke(0).Invoke());
                actualStackAddress = State.StackPhysicalAddress;
                actualReturnCs = State.CS;
                actualReturnIp = State.IP;
            } else {
                throw FailAsUntested(message);
            }
        }
    }

    private void ExecuteCall(Func<int, Action> function, Action action) {
        JumpDispatcher currentJumpDispatcher = JumpDispatcher;
        // Ensure the jump dispatcher has the function we are calling as starting point
        JumpDispatcher = new JumpDispatcher(function);
        action.Invoke();
        JumpDispatcher = currentJumpDispatcher;
    }

    public void OverrideInstruction(ushort segment, ushort offset, Func<Action> renamedOverride) {
        AddressBreakPoint breakPoint = new(
            BreakPointType.EXECUTION,
            MemoryUtils.ToPhysicalAddress(
                segment,
                offset),
            _ => renamedOverride.Invoke().Invoke()
            , false);
        Machine.MachineBreakpoints.ToggleBreakPoint(breakPoint, true);
    }

    public void DoOnTopOfInstruction(ushort segment, ushort offset, Action action) {
        AddressBreakPoint breakPoint = new(
            BreakPointType.EXECUTION,
            MemoryUtils.ToPhysicalAddress(
                segment,
                offset),
            _ => action.Invoke()
            , false);
        Machine.MachineBreakpoints.ToggleBreakPoint(breakPoint, true);
    }

    /// <summary>
    /// Define functions for provided interrupt handlers, so that when overriden code generates an interrupt, it is executed.
    /// </summary>
    public void SetProvidedInterruptHandlersAsOverridden() {
        CallbackHandler callbackHandler = Machine.CallbackHandler;
        foreach (KeyValuePair<byte, SegmentedAddress> callbackAddressEntry in callbackHandler.GetCallbackAddresses()) {
            byte callbackNumber = callbackAddressEntry.Key;
            SegmentedAddress callbackAddress = callbackAddressEntry.Value;
            Func<int, Action> runnable = new Func<int, Action>(_ => {
                callbackHandler.Run(callbackNumber);
                return InterruptRet();
            });
            DefineFunction(callbackAddress.Segment, callbackAddress.Offset, runnable, false,
                $"provided_interrupt_handler_{ConvertUtils.ToHex(callbackNumber)}");
        }
    }

    public void CheckVtableContainsExpected(int segmentRegisterIndex,
        ushort offset,
        ushort expectedSegment,
        ushort expectedOffset) {
        uint address = MemoryUtils.ToPhysicalAddress(State.SegmentRegisters.GetRegister16(segmentRegisterIndex), offset);
        ushort foundOffset = Memory.GetUint16(address);
        ushort foundSegment = Memory.GetUint16(address + 2);
        if (foundOffset != expectedOffset || foundSegment != expectedSegment) {
            throw FailAsUntested(
                $"Call table value changed, we would not call the method the game is calling. Expected: {new SegmentedAddress(expectedSegment, expectedOffset)} found: {new SegmentedAddress(foundSegment, foundOffset)}");
        }
    }

    public void DefineExecutableArea(uint startAddress, uint endAddress) {
        for (uint address = startAddress; address <= endAddress; address++) {
            Cpu.ExecutionFlowRecorder.RegisterExecutableByteModificationBreakPoint(Machine, address);
        }
    }

    /// <summary>
    /// Call this in your override when you re-implement a function with a branch that seems never
    /// reached.
    /// </summary>
    public UnrecoverableException FailAsUntested(string message) {
        string error =
            $"Untested code reached, please tell us how to reach this state. Here is the message: {message}. Here is the Machine stack: {State}";
        if (_loggerService.IsEnabled(LogEventLevel.Error)) {
            _loggerService.Error("{Error}", error);
        }

        return new UnrecoverableException(error);
    }

    public void FailIfValueIsNot(uint value, params uint[] possibleValues) {
        if (!possibleValues.Contains(value)) {
            throw FailAsUntested($"Value {value} not in list of supported values");
        }
    }

    public void CheckExternalEvents(ushort expectedReturnCs, ushort expectedReturnIp) {
        if (!Cpu.IsRunning) {
            Exit();
        }
        State.IncCycles();
        Machine.Timer.Tick();
        if (!InterruptFlag) {
            return;
        }
        byte? vectorNumber = Machine.DualPic.ComputeVectorNumber();
        if (vectorNumber != null) {
            InterruptCall(expectedReturnCs, expectedReturnIp, vectorNumber.Value);
        }
    }

    public void Interrupt(byte vectorNumber) {
        Machine.CallbackHandler.RunFromOverriden(vectorNumber);
    }

    public Action Hlt() => () => Exit();

    protected void Exit() {
        _loggerService.Information("Program requested exit. Terminating now.");
        throw new HaltRequestedException();
    }
}