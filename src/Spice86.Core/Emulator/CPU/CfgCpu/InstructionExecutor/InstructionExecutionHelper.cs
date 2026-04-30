namespace Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;

using System.Numerics;

using Serilog.Events;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.Linker;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
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
    private readonly EmulatorBreakpointsManager _emulatorBreakpointsManager;
    private readonly ExecutionContextManager _executionContextManager;
    private readonly ReturnOperationsHelper _returnOperationsHelper;
    private readonly bool _failOnInvalidOpcode;
    private readonly bool _allowIvtAddress0;
    public InstructionExecutionHelper(State state,
        IMemory memory,
        IOPortDispatcher ioPortDispatcher,
        CallbackHandler callbackHandler,
        EmulatorBreakpointsManager emulatorBreakpointsManager,
        ExecutionContextManager executionContextManager,
        bool failOnInvalidOpcode,
        bool allowIvtAddress0,
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
        _failOnInvalidOpcode = failOnInvalidOpcode;
        _allowIvtAddress0 = allowIvtAddress0;
        _returnOperationsHelper = new (state, Stack);
    }
    public State State { get; }
    public IMemory Memory{ get; }
    public InterruptVectorTable InterruptVectorTable { get; }
    public Stack Stack { get; }
    public IOPortDispatcher IoPortDispatcher { get; }
    public CallbackHandler CallbackHandler { get; }
    public Alu8 Alu8 { get; }
    public Alu16 Alu16 { get; }
    public Alu32 Alu32 { get; }
    public UInt16RegistersIndexer UInt16Registers => State.GeneralRegisters.UInt16;
    public UInt32RegistersIndexer UInt32Registers => State.GeneralRegisters.UInt32;
    public UInt16RegistersIndexer SegmentRegisters => State.SegmentRegisters.UInt16;
    private FunctionHandler CurrentFunctionHandler => _executionContextManager.CurrentExecutionContext.FunctionHandler;
    private ExecutionContext CurrentExecutionContext => _executionContextManager.CurrentExecutionContext;

    // Real mode: jump targets are already truncated to 16-bit IP by the parser/AST
    public void JumpFar(CfgInstruction instruction, ushort cs, ushort ip) {
        State.CS = cs;
        State.IP = ip;
    }

    public void JumpNear(CfgInstruction instruction, ushort ip) {
        State.IP = ip;
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
        SegmentedAddress returnAddress = instruction.NextInMemoryAddress32.ToSegmentedAddress();
        Stack.PushSegmentedAddress(returnAddress);
        HandleCall(instruction, CallType.FAR16, returnAddress, target);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void FarCallWithReturnIpNextInstruction32(CfgInstruction instruction, SegmentedAddress32 target) {
        Stack.PushFarPointer32(instruction.NextInMemoryAddress32);
        HandleCall(instruction, CallType.FAR32, instruction.NextInMemoryAddress32.ToSegmentedAddress(), target.ToSegmentedAddress());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void HandleCall(CfgInstruction instruction,
        CallType callType,
        SegmentedAddress returnAddress,
        SegmentedAddress target) {
        State.CS = target.Segment;
        State.IP = target.Offset;
        CurrentFunctionHandler.Call(callType, target, returnAddress, instruction);
    }

    /// <summary>
    /// Moves IP to end of instruction and does an interrupt call
    /// </summary>
    /// <param name="instruction"></param>
    /// <param name="vectorNumber"></param>
    public void HandleInterruptInstruction(CfgInstruction instruction, byte vectorNumber) {
        // Trigger breakpoint before modifying State.IP.
        // The UI's breakpoint action calls WaitIfPaused() to block until user resumes
        // This ensures the debugger sees State.IP pointing to the INT instruction
        _emulatorBreakpointsManager.InterruptBreakPoints.TriggerMatchingBreakPoints(vectorNumber);
        MoveIpToEndOfInstruction(instruction);
        (SegmentedAddress target, SegmentedAddress expectedReturn) = DoInterruptWithoutBreakpoint(vectorNumber);
        CurrentFunctionHandler.ICall(target, expectedReturn, instruction, vectorNumber);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void HandleInterruptCall(CfgInstruction instruction, byte vectorNumber) {
        (SegmentedAddress target, SegmentedAddress expectedReturn) = DoInterrupt(vectorNumber);
        CurrentFunctionHandler.ICall(target, expectedReturn, instruction, vectorNumber);
    }
    
    public (SegmentedAddress, SegmentedAddress) DoInterrupt(byte vectorNumber) {
        _emulatorBreakpointsManager.InterruptBreakPoints.TriggerMatchingBreakPoints(vectorNumber);
        return DoInterruptWithoutBreakpoint(vectorNumber);
    }

    private (SegmentedAddress, SegmentedAddress) DoInterruptWithoutBreakpoint(byte vectorNumber) {
        SegmentedAddress target = InterruptVectorTable[vectorNumber];
        if (target.Segment == 0 && target.Offset == 0 && !_allowIvtAddress0) {
            throw new UnhandledOperationException(State,
                $"Interrupt vector 0x{vectorNumber:X2} points to 0:0 (uninitialized). Use --AllowIvtAddress0 to permit this.");
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
    public void HandleInterruptRet(CfgInstruction instruction) {
        CurrentFunctionHandler.Ret(CallType.INTERRUPT, instruction);
        _returnOperationsHelper.InterruptRet();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void HandleInterruptRet32(CfgInstruction instruction) {
        CurrentFunctionHandler.Ret(CallType.INTERRUPT, instruction);
        _returnOperationsHelper.InterruptRet32();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void HandleNearRet16(CfgInstruction instruction, ushort numberOfBytesToPop = 0) {
        CurrentFunctionHandler.Ret(CallType.NEAR16, instruction);
        _returnOperationsHelper.NearRet16(numberOfBytesToPop);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void HandleNearRet32(CfgInstruction instruction, ushort numberOfBytesToPop = 0) {
        CurrentFunctionHandler.Ret(CallType.NEAR32, instruction);
        _returnOperationsHelper.NearRet32(numberOfBytesToPop);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void HandleFarRet16(CfgInstruction instruction, ushort numberOfBytesToPop = 0) {
        CurrentFunctionHandler.Ret(CallType.FAR16, instruction);
        _returnOperationsHelper.FarRet16(numberOfBytesToPop);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void HandleFarRet32(CfgInstruction instruction, ushort numberOfBytesToPop = 0) {
        CurrentFunctionHandler.Ret(CallType.FAR32, instruction);
        _returnOperationsHelper.FarRet32(numberOfBytesToPop);
    }

    public void MoveIpToEndOfInstruction(CfgInstruction instruction) {
        State.IP = (ushort)instruction.NextInMemoryAddress32.Offset;
    }

    public void ExecuteHlt(CfgInstruction instruction) {
        State.IsRunning = false;
        MoveIpToEndOfInstruction(instruction);
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

    public void HandleCpuException(CfgInstruction instruction, CpuException cpuException) {
        // Check if this is an invalid opcode exception and we should fail the emulator
        if (_failOnInvalidOpcode && cpuException is CpuInvalidOpcodeException) {
            throw new InvalidVMOperationException(State, cpuException);
        }

        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug(cpuException,"{ExceptionType} in {MethodName}", nameof(CpuException), nameof(HandleCpuException));
        }
        // Real-mode interrupts do NOT push an error code on the stack — that is a
        // protected-mode behavior. Spice86 is real-mode only, so any error code
        // carried by the exception object is informational and must not be pushed.
        try {
            // Link to the interrupt handler will likely need to be added
            instruction.IncreaseMaxSuccessorsCount(InterruptVectorTable[cpuException.InterruptVector]);
            HandleInterruptCall(instruction, cpuException.InterruptVector);
            CurrentExecutionContext.CpuFault = true;
        } catch (UnhandledOperationException e) {
            throw new AggregateException(cpuException, e);
        }
    }

    /// <summary>
    /// Finds the index of the first set bit from the right (least significant bit).
    /// Used for BSF (Bit Scan Forward) instruction.
    /// </summary>
    /// <param name="value">The value to scan (16-bit).</param>
    /// <returns>The bit index (0-15) of the first set bit from the right.</returns>
    public ushort BitScanForward16(ushort value) {
        return (ushort)BitOperations.TrailingZeroCount(value);
    }

    /// <summary>
    /// Finds the index of the first set bit from the right (least significant bit).
    /// Used for BSF (Bit Scan Forward) instruction.
    /// </summary>
    /// <param name="value">The value to scan (32-bit).</param>
    /// <returns>The bit index (0-31) of the first set bit from the right.</returns>
    public uint BitScanForward32(uint value) {
        return (uint)BitOperations.TrailingZeroCount(value);
    }

    /// <summary>
    /// Finds the index of the first set bit from the left (most significant bit).
    /// Used for BSR (Bit Scan Reverse) instruction.
    /// </summary>
    /// <param name="value">The value to scan (16-bit).</param>
    /// <returns>The bit index (0-15) of the first set bit from the left.</returns>
    public ushort BitScanReverse16(ushort value) {
        return (ushort)BitOperations.Log2(value);
    }

    /// <summary>
    /// Finds the index of the first set bit from the left (most significant bit).
    /// Used for BSR (Bit Scan Reverse) instruction.
    /// </summary>
    /// <param name="value">The value to scan (32-bit).</param>
    /// <returns>The bit index (0-31) of the first set bit from the left.</returns>
    public uint BitScanReverse32(uint value) {
        return (uint)BitOperations.Log2(value);
    }

    /// <summary>
    /// Sets the InterruptShadowing flag on State, preventing interrupts for one instruction cycle.
    /// Used by instructions that load SS (e.g., LSS) to ensure SP is also updated safely.
    /// </summary>
    public void SetInterruptShadowing() {
        State.InterruptShadowing = true;
    }

    public void ExecuteCpuid(CfgInstruction instruction) {
        throw new CpuInvalidOpcodeException("Attempted to call CPUID, which is unsupported on CPUs < 486");
    }

    /// <summary>
    /// Executes a callback by number, then advances IP if the callback did not perform a jump.
    /// </summary>
    /// <param name="instruction">The instruction being executed (for IP comparison and advancement).</param>
    /// <param name="callbackNumber">The callback number to dispatch.</param>
    public void ExecuteCallback(CfgInstruction instruction, ushort callbackNumber) {
        CallbackHandler.Run(callbackNumber);
        if (State.IpSegmentedAddress == instruction.Address) {
            MoveIpToEndOfInstruction(instruction);
        }
    }

    /// <summary>
    /// Conditionally sets InterruptShadowing when interrupts are currently disabled.
    /// Per the Intel spec, executing STI when IF=0 blocks maskable interrupts for one additional instruction cycle.
    /// </summary>
    public void SetInterruptShadowingIfInterruptDisabled() {
        if (!State.InterruptFlag) {
            State.InterruptShadowing = true;
        }
    }

    /// <summary>
    /// Checks that a 16-bit signed index lies within [lower, upper] (inclusive).
    /// Throws <see cref="CpuBoundRangeExceededException"/> if the check fails.
    /// </summary>
    public void CheckBound(short index, short lower, short upper) {
        if (index < lower || index > upper) {
            throw new CpuBoundRangeExceededException(
                $"BOUND check failed: index={index}, lower={lower}, upper={upper}.");
        }
    }

    /// <summary>
    /// Checks that a 32-bit signed index lies within [lower, upper] (inclusive).
    /// Throws <see cref="CpuBoundRangeExceededException"/> if the check fails.
    /// </summary>
    public void CheckBound(int index, int lower, int upper) {
        if (index < lower || index > upper) {
            throw new CpuBoundRangeExceededException(
                $"BOUND check failed: index={index}, lower={lower}, upper={upper}.");
        }
    }
}