namespace Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;

using Serilog.Events;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.Linker;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.CommonGrammar;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Interfaces;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Common.Callback;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System.Runtime.CompilerServices;

public class InstructionExecutionHelper {
    private readonly ILoggerService _loggerService;
    private readonly EmulatorBreakpointsManager _emulatorBreakpointsManager;
    private readonly ExecutionContextManager _executionContextManager;

    public InstructionExecutionHelper(State state,
        IMemory memory,
        IOPortDispatcher ioPortDispatcher,
        CallbackHandler callbackHandler,
        EmulatorBreakpointsManager emulatorBreakpointsManager,
        ExecutionContextManager executionContextManager,
        ILoggerService loggerService) {
        _loggerService = loggerService;
        State = state;
        Memory = memory;
        InterruptVectorTable = new(memory);
        Stack = new Stack(memory, state);
        Alu8 = new(state);
        Alu16 = new(state);
        Alu32 = new(state);
        IoPortDispatcher = ioPortDispatcher;
        CallbackHandler = callbackHandler;
        _emulatorBreakpointsManager = emulatorBreakpointsManager;
        _executionContextManager = executionContextManager;
        InstructionFieldValueRetriever = new(memory);
        ModRm = new(state, memory, InstructionFieldValueRetriever);
    }
    public State State { get; }
    public IMemory Memory{ get; }
    public InterruptVectorTable InterruptVectorTable { get; }
    public Stack Stack { get; }
    public IOPortDispatcher IoPortDispatcher { get; }
    public CallbackHandler CallbackHandler { get; }
    public InstructionFieldValueRetriever InstructionFieldValueRetriever { get; }
    public ModRmExecutor ModRm { get; }
    public Alu8 Alu8 { get; }
    public Alu16 Alu16 { get; }
    public Alu32 Alu32 { get; }
    public UInt16RegistersIndexer UInt16Registers => State.GeneralRegisters.UInt16;
    public UInt32RegistersIndexer UInt32Registers => State.GeneralRegisters.UInt32;
    public UInt16RegistersIndexer SegmentRegisters => State.SegmentRegisters.UInt16;
    private FunctionHandler CurrentFunctionHandler => _executionContextManager.CurrentExecutionContext.FunctionHandler;
    private ExecutionContext CurrentExecutionContext => _executionContextManager.CurrentExecutionContext;
    public ICfgNode? NextNode { get; set; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ushort SegmentValue(IInstructionWithSegmentRegisterIndex instruction) {
        return State.SegmentRegisters.UInt16[instruction.SegmentRegisterIndex];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint PhysicalAddress(IInstructionWithSegmentRegisterIndex instruction, ushort offset) {
        return MemoryUtils.ToPhysicalAddress(SegmentValue(instruction), offset);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint PhysicalAddress(IInstructionWithSegmentRegisterIndex instruction, uint offset) {
        if (offset > 0xFFFF) {
            throw new CpuGeneralProtectionFaultException("Offset overflows 16 bits");
        }
        return MemoryUtils.ToPhysicalAddress(SegmentValue(instruction), (ushort)offset);
    }

    public ushort UShortOffsetValue(IInstructionWithOffsetField<ushort> instruction) {
        return InstructionFieldValueRetriever.GetFieldValue(instruction.OffsetField);
    }

    public SegmentedAddress GetSegmentedAddress(InstructionWithSegmentRegisterIndexAndOffsetField<ushort> instruction) {
        ushort segment = SegmentValue(instruction);
        ushort offset = UShortOffsetValue(instruction);
        return new SegmentedAddress(segment, offset);
    }

    public void JumpFar(CfgInstruction instruction, ushort cs, ushort ip) {
        State.CS = cs;
        State.IP = ip;
        SetNextNodeToSuccessorAtCsIp(instruction);
    }

    public void JumpNear(CfgInstruction instruction, ushort ip) {
        State.IP = ip;
        SetNextNodeToSuccessorAtCsIp(instruction);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void NearCallWithReturnIpNextInstruction16(CfgInstruction instruction, ushort callIP) {
        MoveIpToEndOfInstruction(instruction);
        Stack.Push16(State.IP);
        HandleCall(instruction, CallType.NEAR16, new SegmentedAddress(State.CS, State.IP),  new SegmentedAddress(State.CS, callIP));
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void NearCallWithReturnIpNextInstruction32(CfgInstruction instruction, ushort callIP) {
        MoveIpToEndOfInstruction(instruction);
        Stack.Push32(State.IP);
        HandleCall(instruction, CallType.NEAR32, new SegmentedAddress(State.CS, State.IP),  new SegmentedAddress(State.CS, callIP));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void FarCallWithReturnIpNextInstruction16(CfgInstruction instruction, SegmentedAddress target) {
        SegmentedAddress returnAddress = instruction.NextInMemoryAddress;
        Stack.PushSegmentedAddress(returnAddress);
        HandleCall(instruction, CallType.FAR16, returnAddress, target);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void FarCallWithReturnIpNextInstruction32(CfgInstruction instruction, SegmentedAddress target) {
        SegmentedAddress returnAddress = instruction.NextInMemoryAddress;
        // CS padding
        Stack.Push16(0);
        Stack.PushSegmentedAddress32(returnAddress);
        HandleCall(instruction, CallType.FAR32, returnAddress, target);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void HandleCall(CfgInstruction instruction,
        CallType callType,
        SegmentedAddress returnAddress,
        SegmentedAddress target) {
        State.CS = target.Segment;
        State.IP = target.Offset;
        CurrentFunctionHandler.Call(callType, target, returnAddress, instruction);
        SetNextNodeToSuccessorAtCsIp(instruction);
    }

    /// <summary>
    /// Moves IP to end of instruction and does an interrupt call
    /// </summary>
    /// <param name="instruction"></param>
    /// <param name="vectorNumber"></param>
    public void HandleInterruptInstruction(CfgInstruction instruction, byte vectorNumber) {
        // Trigger breakpoint before modifying State.IP
        // The UI's breakpoint action calls WaitIfPaused() to block until user resumes
        // This ensures the debugger sees State.IP pointing to the INT instruction
        _emulatorBreakpointsManager.InterruptBreakPoints.TriggerMatchingBreakPoints(vectorNumber);
        MoveIpToEndOfInstruction(instruction);
        (SegmentedAddress target, SegmentedAddress expectedReturn) = DoInterruptWithoutBreakpoint(vectorNumber);
        CurrentFunctionHandler.ICall(target, expectedReturn, instruction, vectorNumber, false);
        SetNextNodeToSuccessorAtCsIp(instruction);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void HandleInterruptCall(CfgInstruction instruction, byte vectorNumber) {
        (SegmentedAddress target, SegmentedAddress expectedReturn) = DoInterrupt(vectorNumber);
        CurrentFunctionHandler.ICall(target, expectedReturn, instruction, vectorNumber, false);
        SetNextNodeToSuccessorAtCsIp(instruction);
    }
    
    public (SegmentedAddress, SegmentedAddress) DoInterrupt(byte vectorNumber) {
        _emulatorBreakpointsManager.InterruptBreakPoints.TriggerMatchingBreakPoints(vectorNumber);
        return DoInterruptWithoutBreakpoint(vectorNumber);
    }

    private (SegmentedAddress, SegmentedAddress) DoInterruptWithoutBreakpoint(byte vectorNumber) {
        SegmentedAddress target = InterruptVectorTable[vectorNumber];
        if (target.Segment == 0 && target.Offset == 0) {
            throw new UnhandledOperationException(State,
                $"Int was called but vector was not initialized for vectorNumber={ConvertUtils.ToHex(vectorNumber)}");
        }
        SegmentedAddress expectedReturn = State.IpSegmentedAddress;
        Stack.Push16(State.Flags.FlagRegister16);
        Stack.PushSegmentedAddress(expectedReturn);
        State.InterruptFlag = false;
        State.IP = target.Offset;
        State.CS = target.Segment;
        return (target, expectedReturn);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void HandleInterruptRet<T>(T instruction) where T : CfgInstruction, IReturnInstruction {
        CurrentFunctionHandler.Ret(CallType.INTERRUPT, instruction);
        State.IpSegmentedAddress = Stack.PopSegmentedAddress();
        State.Flags.FlagRegister = Stack.Pop16();
        SetNextNodeToSuccessorAtCsIp(instruction);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void HandleNearRet16<T>(T instruction, int numberOfBytesToPop = 0) where T : CfgInstruction, IReturnInstruction {
        CurrentFunctionHandler.Ret(CallType.NEAR16, instruction);
        State.IP = Stack.Pop16();
        Stack.Discard(numberOfBytesToPop);
        SetNextNodeToSuccessorAtCsIp(instruction);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void HandleNearRet32<T>(T instruction, int numberOfBytesToPop = 0) where T : CfgInstruction, IReturnInstruction {
        CurrentFunctionHandler.Ret(CallType.NEAR32, instruction);
        State.IP = (ushort)Stack.Pop32();
        Stack.Discard(numberOfBytesToPop);
        SetNextNodeToSuccessorAtCsIp(instruction);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void HandleFarRet16<T>(T instruction, int numberOfBytesToPop = 0) where T : CfgInstruction, IReturnInstruction {
        CurrentFunctionHandler.Ret(CallType.FAR16, instruction);
        State.IpSegmentedAddress = Stack.PopSegmentedAddress();
        Stack.Discard(numberOfBytesToPop);
        SetNextNodeToSuccessorAtCsIp(instruction);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void HandleFarRet32<T>(T instruction, int numberOfBytesToPop = 0) where T : CfgInstruction, IReturnInstruction {
        CurrentFunctionHandler.Ret(CallType.FAR32, instruction);
        State.IpSegmentedAddress = Stack.PopSegmentedAddress32();
        // CS padding, discard at least 2
        Stack.Discard(numberOfBytesToPop + 2);
        SetNextNodeToSuccessorAtCsIp(instruction);
    }

    public void MoveIpToEndOfInstruction(CfgInstruction instruction) {
        State.IP = (ushort)(State.IP + instruction.Length);
    }

    public ICfgNode? GetSuccessorAtCsIp(CfgInstruction instruction) {
        if (instruction.UniqueSuccessor is not null) {
            return instruction.UniqueSuccessor;
        }
        instruction.SuccessorsPerAddress.TryGetValue(State.IpSegmentedAddress, out ICfgNode? res);
        return res;
    }

    public void SetNextNodeToSuccessorAtCsIp(CfgInstruction instruction) {
        NextNode = GetSuccessorAtCsIp(instruction);
    }

    public void MoveIpAndSetNextNode(CfgInstruction instruction) {
        MoveIpToEndOfInstruction(instruction);
        SetNextNodeToSuccessorAtCsIp(instruction);
    }

    public byte In8(ushort port) {
        return IoPortDispatcher.ReadByte(port);
    }

    public ushort In16(ushort port) {
        return IoPortDispatcher.ReadWord(port);
    }

    public uint In32(ushort port) {
        return IoPortDispatcher.ReadDWord(port);
    }

    public void Out8(ushort port, byte val) => IoPortDispatcher.WriteByte(port, val);

    public void Out16(ushort port, ushort val) => IoPortDispatcher.WriteWord(port, val);

    public void Out32(ushort port, uint val) => IoPortDispatcher.WriteDWord(port, val);
    
    public uint MemoryAddressEsDi => MemoryUtils.ToPhysicalAddress(State.ES, State.DI);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint GetMemoryAddressOverridableDsSi(IInstructionWithSegmentRegisterIndex instruction) {
        return PhysicalAddress(instruction, State.SI);
    }
    
    public void AdvanceSI(short diff) {
        State.SI = (ushort)(State.SI + diff);
    }

    public void AdvanceDI(short diff) {
        State.DI = (ushort)(State.DI + diff);
    }
    public void AdvanceSIDI(short diff) {
        AdvanceSI(diff);
        AdvanceDI(diff);
    }
    
    public void HandleCpuException(CfgInstruction instruction, CpuException cpuException) {
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug(cpuException,"{ExceptionType} in {MethodName}", nameof(CpuException), nameof(HandleCpuException));
        }
        if (cpuException.ErrorCode != null) {
            Stack.Push16(cpuException.ErrorCode.Value);
        }
        try {
            // Link to the interrupt handler will likely need to be added
            instruction.IncreaseMaxSuccessorsCount(InterruptVectorTable[cpuException.InterruptVector]);
            HandleInterruptCall(instruction, cpuException.InterruptVector);
            CurrentExecutionContext.CpuFault = true;
        } catch (UnhandledOperationException e) {
            throw new AggregateException(cpuException, e);
        }
    }

    public void ExecuteStringOperation(StringInstruction instruction) {
        RepPrefix? repPrefix = instruction.RepPrefix;
        if (repPrefix == null) {
            instruction.ExecuteStringOperation(this);
        } else {
            // For some instructions, zero flag is not to be checked
            bool checkZeroFlag = instruction.ChangesFlags;
            ushort cx = State.CX;
            while (cx != 0) {
                instruction.ExecuteStringOperation(this);
                cx--;
                // Not all the string operations require checking the zero flag...
                if (checkZeroFlag && State.ZeroFlag != repPrefix.ContinueZeroFlagValue) {
                    break;
                }
            }
            State.CX = cx;
        }
    }
}