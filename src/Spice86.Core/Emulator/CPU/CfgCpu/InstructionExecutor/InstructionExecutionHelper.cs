namespace Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;

using Serilog.Events;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
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
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System.Runtime.CompilerServices;

public class InstructionExecutionHelper {
    private readonly ILoggerService _loggerService;
    private readonly BreakPointHolder _interruptBreakPoints;
    private readonly ExecutionContextManager _executionContextManager;

    public InstructionExecutionHelper(State state,
        IMemory memory,
        IOPortDispatcher ioPortDispatcher,
        CallbackHandler callbackHandler,
        BreakPointHolder interruptBreakPoints,
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
        _interruptBreakPoints = interruptBreakPoints;
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

    public ICfgNode? NextNode { get; set; }
    
    public ushort SegmentValue(IInstructionWithSegmentRegisterIndex instruction) {
        return State.SegmentRegisters.UInt16[instruction.SegmentRegisterIndex];
    }

    public uint PhysicalAddress(IInstructionWithSegmentRegisterIndex instruction, ushort offset) {
        return MemoryUtils.ToPhysicalAddress(SegmentValue(instruction), offset);
    }

    public ushort UShortOffsetValue(IInstructionWithOffsetField<ushort> instruction) {
        return InstructionFieldValueRetriever.GetFieldValue(instruction.OffsetField);
    }

    public SegmentedAddress GetSegmentedAddress(InstructionWithSegmentRegisterIndexAndOffsetField<ushort> instruction) {
        ushort segment = SegmentValue(instruction);
        ushort offset = UShortOffsetValue(instruction);
        return new SegmentedAddress(segment, offset);
    }

    public void JumpNearOffset(CfgInstruction instruction, int offset) {
        MoveIpToEndOfInstruction(instruction);
        State.IP = (ushort)(State.IP + offset);
        SetNextNodeToSuccessorAtCsIp(instruction);
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
    public void NearCallOffset(CfgInstruction instruction, int offset) {
        MoveIpToEndOfInstruction(instruction);
        ushort callIP = (ushort)(State.IP + offset);
        NearCall(instruction, State.IP, callIP);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void NearCallWithReturnIpNextInstruction(CfgInstruction instruction, ushort callIP) {
        MoveIpToEndOfInstruction(instruction);
        NearCall(instruction, State.IP, callIP);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void NearCall(CfgInstruction instruction, ushort returnIP, ushort callIP) {
        Stack.Push16(returnIP);
        HandleCall(instruction, CallType.NEAR, new SegmentedAddress(State.CS, returnIP),  new SegmentedAddress(State.CS, callIP));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void FarCallWithReturnIpNextInstruction(CfgInstruction instruction, SegmentedAddress target) {
        MoveIpToEndOfInstruction(instruction);
        FarCall(instruction, State.IpSegmentedAddress, target);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void FarCall(CfgInstruction instruction,
        SegmentedAddress returnAddress,
        SegmentedAddress target) {
        Stack.PushSegmentedAddress(returnAddress);
        HandleCall(instruction, CallType.FAR, returnAddress, target);
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
        MoveIpToEndOfInstruction(instruction);
        HandleInterruptCall(instruction, vectorNumber);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void HandleInterruptCall(CfgInstruction instruction, byte vectorNumber) {
        (SegmentedAddress target, SegmentedAddress expectedReturn) = DoInterrupt(vectorNumber);
        CurrentFunctionHandler.ICall(target, expectedReturn, instruction, vectorNumber, false);
        SetNextNodeToSuccessorAtCsIp(instruction);
    }
    
    public (SegmentedAddress, SegmentedAddress) DoInterrupt(byte vectorNumber) {
        _interruptBreakPoints.TriggerMatchingBreakPoints(vectorNumber);
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
    public void HandleNearRet<T>(T instruction, int numberOfBytesToPop = 0) where T : CfgInstruction, IReturnInstruction {
        CurrentFunctionHandler.Ret(CallType.NEAR, instruction);
        State.IP = Stack.Pop16();
        Stack.Discard(numberOfBytesToPop);
        SetNextNodeToSuccessorAtCsIp(instruction);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void HandleFarRet<T>(T instruction, int numberOfBytesToPop = 0) where T : CfgInstruction, IReturnInstruction {
        CurrentFunctionHandler.Ret(CallType.FAR, instruction);
        State.IpSegmentedAddress = Stack.PopSegmentedAddress();
        Stack.Discard(numberOfBytesToPop);
        SetNextNodeToSuccessorAtCsIp(instruction);
    }

    public void MoveIpToEndOfInstruction(CfgInstruction instruction) {
        State.IP = (ushort)(State.IP + instruction.Length);
    }

    public ICfgNode? GetSuccessorAtCsIp(CfgInstruction instruction) {
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
            HandleInterruptCall(instruction, cpuException.InterruptVector);
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