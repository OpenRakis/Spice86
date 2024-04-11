namespace Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.AddRegRm;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.AddRmReg;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Grp1;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Interfaces;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.JmpNearImm;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.MovMoffsAcc;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.MovRegImm;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.MovRmImm;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.MovRmReg;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.PushPop;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.SelfModifying;
using Spice86.Core.Emulator.InterruptHandlers.Common.Callback;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Emulator.Memory;

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
    private readonly Stack _stack;
    private readonly IOPortDispatcher? _ioPortDispatcher;
    private readonly CallbackHandler _callbackHandler;
    private readonly InstructionFieldValueRetriever _instructionFieldValueRetriever;
    private readonly ModRmExecutor _modRm;
    private readonly Alu8 _alu8;
    private readonly Alu16 _alu16;
    private readonly Alu32 _alu32;

    public ExecutorCfgNodeVisitor(State state, IMemory memory, IOPortDispatcher? ioPortDispatcher,
        CallbackHandler callbackHandler) {
        _state = state;
        _memory = memory;
        _stack = new Stack(_memory, state);
        _alu8 = new(state);
        _alu16 = new(state);
        _alu32 = new(state);
        _ioPortDispatcher = ioPortDispatcher;
        _callbackHandler = callbackHandler;
        _instructionFieldValueRetriever = new(_memory);
        _modRm = new(state, memory, _instructionFieldValueRetriever);
    }

    public ICfgNode? NextNode { get; private set; }

    public void Accept(AddRmReg8 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM8 = _alu8.Add(_modRm.RM8, _modRm.R8);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(AddRmReg16 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM16 = _alu16.Add(_modRm.RM16, _modRm.R16);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(AddRmReg32 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM32 = _alu32.Add(_modRm.RM32, _modRm.R32);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(AddRegRm8 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.R8 = _alu8.Add(_modRm.R8, _modRm.RM8);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(AddRegRm16 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.R16 = _alu16.Add(_modRm.R16, _modRm.RM16);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(AddRegRm32 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.R32 = _alu32.Add(_modRm.R32, _modRm.RM32);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(AddAccImm8 instruction) {
        _state.AL = _alu8.Add(_state.AL, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(AddAccImm16 instruction) {
        _state.AX = _alu16.Add(_state.AX, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(AddAccImm32 instruction) {
        _state.EAX = _alu32.Add(_state.EAX, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1Adc8 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM8 = _alu8.Adc(_modRm.RM8, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1AdcSigned16 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM16 = _alu16.Adc(_modRm.RM16, (ushort)_instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1AdcSigned32 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM32 = _alu32.Adc(_modRm.RM32, (uint)_instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1AdcUnsigned16 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM16 = _alu16.Adc(_modRm.RM16, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1AdcUnsigned32 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM32 = _alu32.Adc(_modRm.RM32, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1Add8 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM8 = _alu8.Add(_modRm.RM8, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1AddSigned16 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM16 = _alu16.Add(_modRm.RM16, (ushort)_instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1AddSigned32 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM32 = _alu32.Add(_modRm.RM32, (uint)_instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1AddUnsigned16 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM16 = _alu16.Add(_modRm.RM16, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1AddUnsigned32 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM32 = _alu32.Add(_modRm.RM32, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1And8 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM8 = _alu8.And(_modRm.RM8, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1AndSigned16 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM16 = _alu16.And(_modRm.RM16, (ushort)_instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1AndSigned32 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM32 = _alu32.And(_modRm.RM32, (uint)_instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1AndUnsigned16 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM16 = _alu16.And(_modRm.RM16, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1AndUnsigned32 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM32 = _alu32.And(_modRm.RM32, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1Cmp8 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _alu8.Sub(_modRm.RM8, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1CmpSigned16 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _alu16.Sub(_modRm.RM16, (ushort)_instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1CmpSigned32 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _alu32.Sub(_modRm.RM32, (uint)_instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1CmpUnsigned16 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _alu16.Sub(_modRm.RM16, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1CmpUnsigned32 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _alu32.Sub(_modRm.RM32, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1Or8 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM8 = _alu8.Or(_modRm.RM8, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1OrSigned16 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM16 = _alu16.Or(_modRm.RM16, (ushort)_instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1OrSigned32 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM32 = _alu32.Or(_modRm.RM32, (uint)_instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1OrUnsigned16 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM16 = _alu16.Or(_modRm.RM16, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1OrUnsigned32 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM32 = _alu32.Or(_modRm.RM32, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1Sbb8 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM8 = _alu8.Sbb(_modRm.RM8, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1SbbSigned16 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM16 = _alu16.Sbb(_modRm.RM16, (ushort)_instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1SbbSigned32 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM32 = _alu32.Sbb(_modRm.RM32, (uint)_instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1SbbUnsigned16 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM16 = _alu16.Sbb(_modRm.RM16, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1SbbUnsigned32 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM32 = _alu32.Sbb(_modRm.RM32, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1Sub8 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM8 = _alu8.Sub(_modRm.RM8, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1SubSigned16 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM16 = _alu16.Sub(_modRm.RM16, (ushort)_instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1SubSigned32 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM32 = _alu32.Sub(_modRm.RM32, (uint)_instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1SubUnsigned16 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM16 = _alu16.Sub(_modRm.RM16, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1SubUnsigned32 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM32 = _alu32.Sub(_modRm.RM32, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1Xor8 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM8 = _alu8.Xor(_modRm.RM8, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1XorSigned16 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM16 = _alu16.Xor(_modRm.RM16, (ushort)_instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1XorSigned32 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM32 = _alu32.Xor(_modRm.RM32, (uint)_instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1XorUnsigned16 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM16 = _alu16.Xor(_modRm.RM16, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Grp1XorUnsigned32 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM32 = _alu32.Xor(_modRm.RM32, _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField));
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(MovRmReg8 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM8 = _modRm.R8;
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(MovRmReg16 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM16 = _modRm.R16;
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(MovRmReg32 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM32 = _modRm.R32;
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Pushf16 instruction) {
        _stack.Push16((ushort)_state.Flags.FlagRegister);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Pushf32 instruction) {
        _stack.Push32(_state.Flags.FlagRegister & 0x00FCFFFF);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Popf16 instruction) {
        _state.Flags.FlagRegister = _stack.Pop16();
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(Popf32 instruction) {
        _state.Flags.FlagRegister = _stack.Pop32();
    }

    public void Accept(MovMoffsAcc8 instruction) {
        _memory.UInt8[GetSegmentedAddress(instruction)] = _state.AL;
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(MovMoffsAcc16 instruction) {
        _memory.UInt16[GetSegmentedAddress(instruction)] = _state.AX;
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(MovMoffsAcc32 instruction) {
        _memory.UInt32[GetSegmentedAddress(instruction)] = _state.EAX;
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(MovRegImm8 instruction) {
        byte value = _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField);
        _state.GeneralRegisters.UInt8HighLow[instruction.RegisterIndex] = value;
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(MovRegImm16 instruction) {
        ushort value = _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField);
        _state.GeneralRegisters.UInt16[instruction.RegisterIndex] = value;
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(MovRegImm32 instruction) {
        uint value = _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField);
        _state.GeneralRegisters.UInt32[instruction.RegisterIndex] = value;
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(MovRmImm8 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM8 = _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(MovRmImm16 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM16 = _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(MovRmImm32 instruction) {
        _modRm.RefreshWithNewModRmContext(instruction.ModRmContext);
        _modRm.RM32 = _instructionFieldValueRetriever.GetFieldValue(instruction.ValueField);
        MoveIpAndSetNextNode(instruction);
    }

    public void Accept(JmpNearImm16 instruction) {
        short offset = _instructionFieldValueRetriever.GetFieldValue(instruction.OffsetField);
        JumpNear(instruction, offset);
        SetNextNodeToSuccessorAtCsIp(instruction);
    }

    public void Accept(JmpNearImm8 instruction) {
        sbyte offset = _instructionFieldValueRetriever.GetFieldValue(instruction.OffsetField);
        JumpNear(instruction, offset);
        SetNextNodeToSuccessorAtCsIp(instruction);
    }

    public void Accept(Hlt instruction) {
        _state.IsRunning = false;
        NextNode = null;
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

    private ushort SegmentValue(IInstructionWithSegmentRegisterIndex instruction) {
        return _state.SegmentRegisters.UInt16[instruction.SegmentRegisterIndex];
    }

    private ushort OffsetValue(IInstructionWithOffsetField instruction) {
        return _instructionFieldValueRetriever.GetFieldValue(instruction.OffsetField);
    }

    private SegmentedAddress GetSegmentedAddress(IInstructionWithSegmentRegisterIndexAndOffsetField instruction) {
        ushort segment = SegmentValue(instruction);
        ushort offset = OffsetValue(instruction);
        return new SegmentedAddress(segment, offset);
    }

    private void JumpNear(CfgInstruction instruction, int offset) {
        MoveIpToEndOfInstruction(instruction);
        _state.IP = (ushort)(_state.IP + offset);
    }

    private void MoveIpToEndOfInstruction(CfgInstruction instruction) {
        _state.IP = (ushort)(_state.IP + instruction.Length);
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