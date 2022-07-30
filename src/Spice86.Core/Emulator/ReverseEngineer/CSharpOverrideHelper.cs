namespace Spice86.Core.Emulator.ReverseEngineer;

using Function.Dump;

using Serilog;

using Spice86.Core.Emulator.Callback;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Core.Utils;
using Spice86.Logging;

using System;
using System.Collections.Generic;
using System.Linq;

public class CSharpOverrideHelper {
    private static readonly ILogger _logger = new Serilogger().Logger.ForContext<CSharpOverrideHelper>();

    public Cpu Cpu => Machine.Cpu;

    public Machine Machine { get; }

    public Memory Memory => Machine.Memory;

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
    public ushort FlagRegister { get => State.Flags.FlagRegister; set => State.Flags.FlagRegister = value; }
    public short Direction8 => (short)(DirectionFlag ? -1 : 1);
    public short Direction16 => (short)(DirectionFlag ? -2 : 2);
    private readonly Dictionary<SegmentedAddress, FunctionInformation> _functionInformations;

    public JumpDispatcher JumpDispatcher { get; set; }

    public bool IsRegisterExecutableCodeModificationEnabled { get; set; } = true;

    public CSharpOverrideHelper(Dictionary<SegmentedAddress, FunctionInformation> functionInformations,
        Machine machine) {
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
        if (existing != null && existing.HasOverride) {
            // Do not overwrite existing code with override
            return;
        }

        string functionName;
        if (name != null) {
            functionName = name;
        } else {
            string methodName = overrideFunc.Method.Name;
            FunctionInformation? parsedFunctionInformation = GhidraSymbolsDumper.NameToFunctionInformation(methodName);
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
        ExecuteCallEnsuringSameStack(expectedReturnCs, expectedReturnIp, function, () => {
            Stack.Push(expectedReturnIp);
            Action returnAction = function.Invoke(0);
            returnAction.Invoke();
        });
    }

    public void FarCall(ushort expectedReturnCs, ushort expectedReturnIp, Func<int, Action> function) {
        ExecuteCallEnsuringSameStack(expectedReturnCs, expectedReturnIp, function, () => {
            Stack.Push(expectedReturnCs);
            Stack.Push(expectedReturnIp);
            Action returnAction = function.Invoke(0);
            returnAction.Invoke();
        });
    }

    public void InterruptCall(ushort expectedReturnCs, ushort expectedReturnIp, Func<int, Action> function) {
        ExecuteCallEnsuringSameStack(expectedReturnCs, expectedReturnIp, function, () => {
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
        var target = new SegmentedAddress(targetCS, targetIP);
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
        while (actualReturnCs != expectedReturnCs ||
               actualReturnIp != expectedReturnIp) {
            var expectedReturn = new SegmentedAddress(expectedReturnCs, expectedReturnIp);
            var actualReturn = new SegmentedAddress(actualReturnCs, actualReturnIp);
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
                _logger.Warning("{Message}", message);
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

    public void CheckVtableContainsExpected(int segmentRegisterIndex,
        ushort offset,
        ushort expectedSegment,
        ushort expectedOffset) {
        uint address = MemoryUtils.ToPhysicalAddress(State.SegmentRegisters.GetRegister(segmentRegisterIndex), offset);
        ushort foundOffset = Memory.GetUint16(address);
        ushort foundSegment = Memory.GetUint16(address + 2);
        if (foundOffset != expectedOffset || foundSegment != expectedSegment) {
            throw FailAsUntested(
                $"Call table value changed, we would not call the method the game is calling. Expected: {new SegmentedAddress(expectedSegment, expectedOffset)} found: {new SegmentedAddress(foundSegment, foundOffset)}");
        }
    }

    public void DefineExecutableArea(uint startAddress, uint endAddress) {
        for (uint address = startAddress; address <= endAddress; address++) {
            // For closure
            uint addressCopy = address;
            var breakPoint = new AddressBreakPoint(BreakPointType.WRITE, address, _ => {
                if (!IsRegisterExecutableCodeModificationEnabled) {
                    return;
                }

                byte oldValue = Memory.UInt8[addressCopy];
                byte newValue = Memory.CurrentlyWritingByte;
                if (oldValue != newValue) {
                    Machine.Cpu.ExecutionFlowRecorder.RegisterExecutableCodeModification(
                        new SegmentedAddress(State.CS, State.IP), addressCopy, oldValue, newValue);
                }
            }, false);
            Machine.MachineBreakpoints.ToggleBreakPoint(breakPoint, true);
        }
    }

    /// <summary>
    /// Call this in your override when you re-implement a function with a branch that seems never
    /// reached.
    /// </summary>
    public UnrecoverableException FailAsUntested(string message) {
        string error =
            $"Untested code reached, please tell us how to reach this state. Here is the message: {message}. Here is the Machine stack: {State}";
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Error)) {
            _logger.Error("{Error}", error);
        }

        return new UnrecoverableException(error);
    }

    public void FailIfValueIsNot(uint value, params uint[] possibleValues) {
        if (!possibleValues.Contains(value)) {
            throw FailAsUntested($"Value {value} not in list of supported values");
        }
    }

    public void CheckExternalEvents(ushort expectedReturnCs, ushort expectedReturnIp) {
        Machine.Timer.Tick();
        if (!InterruptFlag) {
            return;
        }
        byte? vectorNumber = Cpu.ExternalInterruptVectorNumber;
        if (vectorNumber != null) {
            InterruptCall(expectedReturnCs, expectedReturnIp, vectorNumber.Value);
            // Reset it so that subsequent interrupts can happen
            Cpu.ExternalInterruptVectorNumber = null;
        }
    }

    public void Interrupt(byte vectorNumber) {
        Machine.CallbackHandler.RunFromOverriden(vectorNumber);
    }

    public Action Hlt() {
        return () => {
            _logger.Information("Program requested exit. Terminating now.");
            Environment.Exit(0);
        };
    }
}