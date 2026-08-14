namespace Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;

using System.Numerics;

using Microsoft.Extensions.Logging;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.Linker;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Core.Emulator.CPU.DescriptorTables;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Common.Callback;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Utils;

using System.Runtime.CompilerServices;

public class InstructionExecutionHelper {
    private readonly ILogger _loggerService;
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
        ILogger loggerService) {
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
        _returnOperationsHelper = new(state, Stack);
    }
    public State State { get; }
    public IMemory Memory { get; }
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
        if (ProtectedModeCallGateDispatcher.TryReadCallGate(State, Memory, cs, out RawGateDescriptor gate)) {
            ProtectedModeCallGateDispatcher.DispatchJump(State, Memory, gate, cs);
            return;
        }
        PrivilegeChecks.ValidateFarCodeSegmentTransfer(State, Memory, cs);
        LoadSegmentRegister((uint)SegmentRegisterIndex.CsIndex, cs);
        State.IP = ip;
    }

    /// <summary>
    /// Loads a raw selector value into a segment register and refreshes its descriptor cache: the
    /// real-mode synthesized cache (base = selector*16) outside protected mode, or the decoded GDT/LDT
    /// descriptor once <see cref="CpuMode.Protected"/> is active. This is the single path every
    /// segment-register write (MOV Sreg, POP Sreg, far transfers) goes through.
    /// </summary>
    public void LoadSegmentRegister(uint segmentRegisterIndex, ushort selector) {
        SegmentAndControlRegisterOperations.LoadSegmentRegister(State, Memory, segmentRegisterIndex, selector);
    }

    /// <summary>LGDT: loads GDTR from a 6-byte memory pointer (2-byte limit, 4-byte base).</summary>
    public void LoadGdtr(ushort segment, uint offset) {
        SegmentAndControlRegisterOperations.LoadGdtr(State, Memory, segment, offset);
    }

    /// <summary>SGDT: stores GDTR to a 6-byte memory pointer (2-byte limit, 4-byte base).</summary>
    public void StoreGdtr(ushort segment, uint offset) {
        SegmentAndControlRegisterOperations.StoreGdtr(State, Memory, segment, offset);
    }

    /// <summary>LIDT: loads IDTR from a 6-byte memory pointer (2-byte limit, 4-byte base).</summary>
    public void LoadIdtr(ushort segment, uint offset) {
        SegmentAndControlRegisterOperations.LoadIdtr(State, Memory, segment, offset);
    }

    /// <summary>SIDT: stores IDTR to a 6-byte memory pointer (2-byte limit, 4-byte base).</summary>
    public void StoreIdtr(ushort segment, uint offset) {
        SegmentAndControlRegisterOperations.StoreIdtr(State, Memory, segment, offset);
    }

    /// <summary>MOV r32, CRn: reads CR0/CR2/CR3/CR4.</summary>
    public uint ReadControlRegister(uint crNumber) {
        return SegmentAndControlRegisterOperations.ReadControlRegister(State, crNumber);
    }

    /// <summary>MOV CRn, r32: writes CR0/CR2/CR3/CR4.</summary>
    public void WriteControlRegister(uint crNumber, uint value) {
        SegmentAndControlRegisterOperations.WriteControlRegister(State, crNumber, value);
    }

    /// <summary>SMSW: reads the low 16 bits of CR0.</summary>
    public ushort ReadMachineStatusWord() {
        return SegmentAndControlRegisterOperations.ReadMachineStatusWord(State);
    }

    /// <summary>LMSW: writes the low 4 bits of CR0 (PE, MP, EM, TS).</summary>
    public void LoadMachineStatusWord(ushort value) {
        SegmentAndControlRegisterOperations.LoadMachineStatusWord(State, value);
    }

    /// <summary>CLTS: clears CR0.TS.</summary>
    public void Clts() {
        SegmentAndControlRegisterOperations.Clts(State);
    }

    /// <summary>Throws #GP if CPL/IOPL do not permit `IN`/`OUT`/`CLI`/`STI`.</summary>
    public void EnsureIoPrivilege() {
        PrivilegeChecks.EnsureIoPrivilege(State);
    }

    /// <summary>LLDT: loads LDTR from a GDT selector.</summary>
    public void LoadLdtr(ushort selector) {
        SegmentAndControlRegisterOperations.LoadLdtr(State, Memory, selector);
    }

    /// <summary>SLDT: reads the current LDTR selector.</summary>
    public ushort StoreLdtr() {
        return SegmentAndControlRegisterOperations.StoreLdtr(State);
    }

    /// <summary>LTR: loads the Task Register from a GDT selector.</summary>
    public void LoadTr(ushort selector) {
        SegmentAndControlRegisterOperations.LoadTr(State, Memory, selector);
    }

    /// <summary>STR: reads the current Task Register selector.</summary>
    public ushort StoreTr() {
        return SegmentAndControlRegisterOperations.StoreTr(State);
    }

    /// <summary>ARPL: returns the r/m operand with its RPL raised to the register operand's RPL if lower.</summary>
    public ushort AdjustRequestedPrivilegeLevel(ushort rmSelector, ushort regSelector) {
        return SegmentAndControlRegisterOperations.AdjustRequestedPrivilegeLevel(rmSelector, regSelector);
    }

    /// <summary>ARPL: whether the r/m operand's RPL was raised (sets ZF).</summary>
    public bool WasPrivilegeLevelAdjusted(ushort rmSelector, ushort regSelector) {
        return SegmentAndControlRegisterOperations.WasPrivilegeLevelAdjusted(rmSelector, regSelector);
    }

    /// <summary>LAR: whether a selector resolves to a present descriptor (sets ZF).</summary>
    public bool IsSelectorValidForLar(ushort selector) {
        return SegmentAndControlRegisterOperations.IsSelectorValidForLar(State, Memory, selector);
    }

    /// <summary>LAR: loads the packed access-rights doubleword for a selector.</summary>
    public uint LoadAccessRights(ushort selector) {
        return SegmentAndControlRegisterOperations.LoadAccessRights(State, Memory, selector);
    }

    /// <summary>LSL: whether a selector resolves to a present segment descriptor (sets ZF).</summary>
    public bool IsSelectorValidForLsl(ushort selector) {
        return SegmentAndControlRegisterOperations.IsSelectorValidForLsl(State, Memory, selector);
    }

    /// <summary>LSL: loads the granularity-scaled limit for a selector.</summary>
    public uint LoadSegmentLimit(ushort selector) {
        return SegmentAndControlRegisterOperations.LoadSegmentLimit(State, Memory, selector);
    }

    /// <summary>VERR: whether a selector is a present, readable data or code segment.</summary>
    public bool VerifyReadable(ushort selector) {
        return SegmentAndControlRegisterOperations.VerifyReadable(State, Memory, selector);
    }

    /// <summary>VERW: whether a selector is a present, writable data segment.</summary>
    public bool VerifyWritable(ushort selector) {
        return SegmentAndControlRegisterOperations.VerifyWritable(State, Memory, selector);
    }

    public void JumpNear(CfgInstruction instruction, ushort ip) {
        State.IP = ip;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void NearCallWithReturnIpNextInstruction16(CfgInstruction instruction, ushort callIP) {
        MoveIpToEndOfInstruction(instruction);
        Stack.Push16(State.IP);
        HandleCall(instruction, CallType.NEAR16, new SegmentedAddress(State.CS, State.IP), new SegmentedAddress(State.CS, callIP));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void NearCallWithReturnIpNextInstruction32(CfgInstruction instruction, ushort callIP) {
        MoveIpToEndOfInstruction(instruction);
        Stack.Push32(State.IP);
        HandleCall(instruction, CallType.NEAR32, new SegmentedAddress(State.CS, State.IP), new SegmentedAddress(State.CS, callIP));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void FarCallWithReturnIpNextInstruction16(CfgInstruction instruction, SegmentedAddress target) {
        SegmentedAddress returnAddress = instruction.NextInMemoryAddress32.ToSegmentedAddress();
        if (TaskSwitchOperations.TryReadAvailableTss(State, Memory, target.Segment)) {
            SegmentedAddress taskTarget = TaskSwitchOperations.SwitchToNewTask(State, Memory, target.Segment, returnAddress.Offset);
            CurrentFunctionHandler.Call(CallType.FAR16, taskTarget, returnAddress, instruction);
            return;
        }
        if (ProtectedModeCallGateDispatcher.TryReadCallGate(State, Memory, target.Segment, out RawGateDescriptor gate)) {
            SegmentedAddress gateTarget = ProtectedModeCallGateDispatcher.Dispatch(State, Memory, Stack, gate, target.Segment, returnAddress);
            CurrentFunctionHandler.Call(CallType.FAR16, gateTarget, returnAddress, instruction);
            return;
        }
        Stack.PushSegmentedAddress(returnAddress);
        HandleCall(instruction, CallType.FAR16, returnAddress, target);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void FarCallWithReturnIpNextInstruction32(CfgInstruction instruction, SegmentedAddress32 target) {
        SegmentedAddress returnAddress = instruction.NextInMemoryAddress32.ToSegmentedAddress();
        if (ProtectedModeCallGateDispatcher.TryReadCallGate(State, Memory, target.Segment, out RawGateDescriptor gate)) {
            SegmentedAddress gateTarget = ProtectedModeCallGateDispatcher.Dispatch(State, Memory, Stack, gate, target.Segment, returnAddress);
            CurrentFunctionHandler.Call(CallType.FAR32, gateTarget, returnAddress, instruction);
            return;
        }
        Stack.PushFarPointer32(instruction.NextInMemoryAddress32);
        HandleCall(instruction, CallType.FAR32, returnAddress, target.ToSegmentedAddress());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void HandleCall(CfgInstruction instruction,
        CallType callType,
        SegmentedAddress returnAddress,
        SegmentedAddress target) {
        if (callType is CallType.FAR16 or CallType.FAR32) {
            PrivilegeChecks.ValidateFarCodeSegmentTransfer(State, Memory, target.Segment);
        }
        LoadSegmentRegister((uint)SegmentRegisterIndex.CsIndex, target.Segment);
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
        (SegmentedAddress target, SegmentedAddress expectedReturn) = DoInterruptWithoutBreakpoint(vectorNumber, checkGateDpl: true);
        CurrentFunctionHandler.ICall(target, expectedReturn, instruction, vectorNumber);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void HandleInterruptCall(CfgInstruction instruction, byte vectorNumber) {
        (SegmentedAddress target, SegmentedAddress expectedReturn) = DoInterrupt(vectorNumber);
        CurrentFunctionHandler.ICall(target, expectedReturn, instruction, vectorNumber);
    }

    public (SegmentedAddress, SegmentedAddress) DoInterrupt(byte vectorNumber, ushort? errorCode = null) {
        _emulatorBreakpointsManager.InterruptBreakPoints.TriggerMatchingBreakPoints(vectorNumber);
        return DoInterruptWithoutBreakpoint(vectorNumber, checkGateDpl: false, errorCode);
    }

    /// <summary>
    /// Dispatches an interrupt or exception. Real mode semantics are unchanged (the real-mode IVT);
    /// protected mode AND Virtual-8086 mode both walk the IDT instead (see
    /// <see cref="ProtectedModeInterruptDispatcher"/>) - on real hardware, V86 code always reflects
    /// interrupts/exceptions to the protected-mode monitor rather than handling them directly, since CPL
    /// is 3 in V86 and the monitor's handlers live at DPL 0, forcing the same escalation-via-TSS path
    /// used by ordinary CPL3-to-CPL0 protected-mode dispatch. <paramref name="checkGateDpl"/> is true only
    /// for a software `INT n`: hardware interrupts and CPU exceptions bypass the gate's DPL.
    /// </summary>
    private (SegmentedAddress, SegmentedAddress) DoInterruptWithoutBreakpoint(byte vectorNumber, bool checkGateDpl, ushort? errorCode = null) {
        if (State.CpuMode is CpuMode.Protected or CpuMode.Virtual8086) {
            SegmentedAddress expectedReturnBeforeDispatch = State.IpSegmentedAddress;
            SegmentedAddress protectedModeTarget = ProtectedModeInterruptDispatcher.Dispatch(
                State, Memory, Stack, vectorNumber, checkGateDpl, errorCode, expectedReturnBeforeDispatch);
            return (protectedModeTarget, expectedReturnBeforeDispatch);
        }
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
        LoadSegmentRegister((uint)SegmentRegisterIndex.CsIndex, target.Segment);
        return (target, expectedReturn);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void HandleInterruptRet(CfgInstruction instruction) {
        CurrentFunctionHandler.Ret(CallType.INTERRUPT, instruction);
        if (State.CpuMode == CpuMode.Protected) {
            ProtectedModeInterruptDispatcher.InterruptReturn16(State, Memory, Stack);
        } else {
            _returnOperationsHelper.InterruptRet();
            LoadSegmentRegister((uint)SegmentRegisterIndex.CsIndex, State.CS);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void HandleInterruptRet32(CfgInstruction instruction) {
        CurrentFunctionHandler.Ret(CallType.INTERRUPT, instruction);
        if (State.CpuMode == CpuMode.Protected) {
            ProtectedModeInterruptDispatcher.InterruptReturn32(State, Memory, Stack);
            return;
        }
        _returnOperationsHelper.InterruptRet32();
        LoadSegmentRegister((uint)SegmentRegisterIndex.CsIndex, State.CS);
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
        if (State.CpuMode == CpuMode.Protected) {
            ProtectedModeInterruptDispatcher.FarReturn16(State, Memory, Stack, numberOfBytesToPop);
        } else {
            _returnOperationsHelper.FarRet16(numberOfBytesToPop);
            LoadSegmentRegister((uint)SegmentRegisterIndex.CsIndex, State.CS);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void HandleFarRet32(CfgInstruction instruction, ushort numberOfBytesToPop = 0) {
        CurrentFunctionHandler.Ret(CallType.FAR32, instruction);
        if (State.CpuMode == CpuMode.Protected) {
            ProtectedModeInterruptDispatcher.FarReturn32(State, Memory, Stack, numberOfBytesToPop);
            return;
        }
        _returnOperationsHelper.FarRet32(numberOfBytesToPop);
        LoadSegmentRegister((uint)SegmentRegisterIndex.CsIndex, State.CS);
    }

    public void MoveIpToEndOfInstruction(CfgInstruction instruction) {
        State.IP = (ushort)instruction.NextInMemoryAddress32.Offset;
    }

    public void ExecuteHlt(CfgInstruction instruction) {
        PrivilegeChecks.EnsureCpl0(State, "HLT");
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
        if (Environment.GetEnvironmentVariable("SPICE86_TRACE_EXC") is not null) {
            System.IO.Directory.CreateDirectory("tmp");
            System.IO.File.AppendAllText("tmp/exc_trace.txt",
                $"vec=0x{cpuException.InterruptVector:X2} type={cpuException.GetType().Name} CS=0x{State.CS:X4} EIP=0x{State.EIP:X8} SS=0x{State.SS:X4} ESP=0x{State.ESP:X8} err={cpuException.ErrorCode} msg={cpuException.Message}\n");
        }
        // Check if this is an invalid opcode exception and we should fail the emulator
        if (_failOnInvalidOpcode && cpuException is CpuInvalidOpcodeException) {
            throw new InvalidVMOperationException(State, cpuException);
        }

        if (_loggerService.IsEnabled(LogLevel.Debug)) {
            _loggerService.LogDebug(cpuException, "{ExceptionType} in {MethodName}", nameof(CpuException), nameof(HandleCpuException));
        }
        // Real mode has no error-code concept; only protected-mode dispatch (DoInterrupt) actually
        // pushes it, and only when the gate/frame layout supports it.
        try {
            // Link to the interrupt handler will likely need to be added
            instruction.IncreaseMaxSuccessorsCount(InterruptVectorTable[cpuException.InterruptVector]);
            (SegmentedAddress target, SegmentedAddress expectedReturn) = DoInterrupt(cpuException.InterruptVector, cpuException.ErrorCode);
            CurrentFunctionHandler.ICall(target, expectedReturn, instruction, cpuException.InterruptVector);
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