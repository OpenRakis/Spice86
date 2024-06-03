namespace Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Interfaces;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Common.Callback;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Emulator.Memory;

public class InstructionExecutionHelper {
    public InstructionExecutionHelper(State state,
        IMemory memory,
        IOPortDispatcher? ioPortDispatcher,
        CallbackHandler callbackHandler) {
        State = state;
        Memory = memory;
        Stack = new Stack(memory, state);
        Alu8 = new(state);
        Alu16 = new(state);
        Alu32 = new(state);
        IoPortDispatcher = ioPortDispatcher;
        CallbackHandler = callbackHandler;
        InstructionFieldValueRetriever = new(memory);
        ModRm = new(state, memory, InstructionFieldValueRetriever);
    }
    public State State { get; }
    public IMemory Memory{ get; }
    public Stack Stack { get; }
    public IOPortDispatcher? IoPortDispatcher { get; }
    public CallbackHandler CallbackHandler { get; }
    public InstructionFieldValueRetriever InstructionFieldValueRetriever { get; }
    public ModRmExecutor ModRm { get; }
    public Alu8 Alu8 { get; }
    public Alu16 Alu16 { get; }
    public Alu32 Alu32 { get; }
    public UInt16RegistersIndexer UInt16Registers => State.GeneralRegisters.UInt16;
    public UInt32RegistersIndexer UInt32Registers => State.GeneralRegisters.UInt32;
    public ICfgNode? NextNode { get; set; }
    
    public ushort SegmentValue(IInstructionWithSegmentRegisterIndex instruction) {
        return State.SegmentRegisters.UInt16[instruction.SegmentRegisterIndex];
    }

    public ushort SegmentValue(IInstructionWithSegmentField instruction) {
        return InstructionFieldValueRetriever.GetFieldValue(instruction.SegmentField);
    }
    
    public ushort OffsetValue(IInstructionWithOffsetField instruction) {
        return InstructionFieldValueRetriever.GetFieldValue(instruction.OffsetField);
    }

    public SegmentedAddress GetSegmentedAddress(IInstructionWithSegmentRegisterIndexAndOffsetField instruction) {
        ushort segment = SegmentValue(instruction);
        ushort offset = OffsetValue(instruction);
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

    public void NearCallWithReturnIpNextInstruction(CfgInstruction instruction, ushort callIP) {
        MoveIpToEndOfInstruction(instruction);
        NearCall(instruction, State.IP, callIP);
    }

    public void NearCall(CfgInstruction instruction, ushort returnIP, ushort callIP) {
        Stack.Push16(returnIP);
        HandleCall(instruction, CallType.NEAR, State.CS, returnIP, State.CS, callIP);
    }

    public void FarCallWithReturnIpNextInstruction(CfgInstruction instruction, ushort targetCS, ushort targetIP) {
        MoveIpToEndOfInstruction(instruction);
        FarCall(instruction, State.CS, State.IP, targetCS, targetIP);
    }

    public void FarCall(CfgInstruction instruction,
        ushort returnCS,
        ushort returnIP,
        ushort targetCS,
        ushort targetIP) {
        Stack.Push16(returnCS);
        Stack.Push16(returnIP);
        HandleCall(instruction, CallType.FAR, returnCS, returnIP, targetCS, targetIP);
    }

    public void HandleCall(CfgInstruction instruction,
        CallType callType,
        ushort returnCS,
        ushort returnIP,
        ushort targetCS,
        ushort targetIP) {
        State.CS = targetCS;
        // Setting it here as well for eventual overrides
        State.IP = targetIP;
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

}