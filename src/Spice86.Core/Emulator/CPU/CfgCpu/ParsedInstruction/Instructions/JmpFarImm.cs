﻿namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.CommonGrammar;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Interfaces;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

public class JmpFarImm : InstructionWithSegmentedAddressField, IJumpInstruction {
    private readonly SegmentedAddress _targetAddress;

    public JmpFarImm(
        SegmentedAddress address,
        InstructionField<ushort> opcodeField,
        List<InstructionPrefix> prefixes, 
        InstructionField<SegmentedAddress> segmentedAddressField) :
        base(address, opcodeField, prefixes, segmentedAddressField, 1) {
        _targetAddress = SegmentedAddressField.Value;
    }

    public override void Execute(InstructionExecutionHelper helper) {
        helper.JumpFar(this, _targetAddress.Segment, _targetAddress.Offset);
    }

    public override InstructionNode ToInstructionAst(AstBuilder builder) {
        return new InstructionNode(InstructionOperation.JMP_FAR, builder.Constant.ToNode(_targetAddress));
    }
}