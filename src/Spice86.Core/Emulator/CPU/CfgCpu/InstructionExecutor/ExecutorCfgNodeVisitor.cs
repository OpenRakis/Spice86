namespace Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.SelfModifying;
using Spice86.Core.Emulator.InterruptHandlers.Common.Callback;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;

/// <summary>
/// Executes the various instructions.
/// This also includes updating CS:IP.
/// After execution, Field NextNode is updated to reflect what was next according to the current graph.
/// If null it means the graph does not have a next node for this path.
/// It will be the job of the CfgCpu to link a new node accordingly.
/// </summary>
public class ExecutorCfgNodeVisitor : ICfgNodeVisitor {
    private readonly State _state;
    private readonly IMemory _memory;
    private readonly IOPortDispatcher? _ioPortDispatcher;
    private readonly CallbackHandler _callbackHandler;
    private readonly InstructionFieldValueRetriever _instructionFieldValueRetriever;

    public ExecutorCfgNodeVisitor(State state, IMemory memory, IOPortDispatcher? ioPortDispatcher,
        CallbackHandler callbackHandler) {
        _state = state;
        _memory = memory;
        _ioPortDispatcher = ioPortDispatcher;
        _callbackHandler = callbackHandler;
        _instructionFieldValueRetriever = new(_memory);
    }
    
    public ICfgNode? NextNode { get; private set; }

    public void Accept(HltInstruction instruction) {
        _state.IsRunning = false;
        NextNode = null;
    }

    public void Accept(JmpNearImm8 instruction) {
        sbyte offset = _instructionFieldValueRetriever.GetFieldValue(instruction.OffsetField);
        JumpNear(instruction, offset);
        SetNextNodeToSuccessorAtCsIp(instruction);
    }

    public void Accept(JmpNearImm16 instruction) {
        short offset = _instructionFieldValueRetriever.GetFieldValue(instruction.OffsetField);
        JumpNear(instruction, offset);
        SetNextNodeToSuccessorAtCsIp(instruction);
    }

    public void Accept(MovRegImm8 instruction) {
        byte value = _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField);
        _state.GeneralRegisters.UInt8HighLow[instruction.RegIndex] = value;
        MoveIpAndSetNextNode(instruction);
    }
    public void Accept(MovRegImm16 instruction) {
        ushort value = _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField);
        _state.GeneralRegisters.UInt16[instruction.RegIndex] = value;
        MoveIpAndSetNextNode(instruction);
    }
    public void Accept(MovRegImm32 instruction) {
        uint value = _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField);
        _state.GeneralRegisters.UInt32[instruction.RegIndex] = value;
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(DiscriminatedNode discriminatedNode) {
        int address = (int)discriminatedNode.Address.ToPhysical();
        foreach (Discriminator discriminator in discriminatedNode.SuccessorsPerDiscriminator.Keys) {
            int length = discriminator.DiscriminatorValue.Count;
            Span<byte> bytes = _memory.GetSpan(address, length);
            if (discriminator.SpanEquivalent(bytes)) {
                NextNode = discriminatedNode.SuccessorsPerDiscriminator[discriminator];
                return;
            }
        }

        NextNode = null;
    }

    private void JumpNear(CfgInstruction instruction, int offset) {
        MoveIpToEndOfInstruction(instruction);
        _state.IP = (ushort)(_state.IP + offset);
    }

    private void MoveIpToEndOfInstruction(CfgInstruction instruction) {
        _state.IP = (ushort)(_state.IP + instruction.Length) ;
    }

    private ICfgNode? GetSuccessorAtCsIp(CfgInstruction instruction) {
        instruction.SuccessorsPerAddress.TryGetValue(_state.IpSegmentedAddress, out ICfgNode? res);
        return res;
    }

    private void SetNextNodeToSuccessorAtCsIp(CfgInstruction instruction) {
        NextNode = GetSuccessorAtCsIp(instruction);
    }

    private void MoveIpAndSetNextNode(CfgInstruction instruction) {
        MoveIpToEndOfInstruction(instruction);
        SetNextNodeToSuccessorAtCsIp(instruction);
    }

}