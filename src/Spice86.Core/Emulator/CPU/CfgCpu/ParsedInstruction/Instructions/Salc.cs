﻿namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Undocumented instruction SALC
/// </summary>
public class Salc : CfgInstruction {

    public Salc(SegmentedAddress address, InstructionField<ushort> opcodeField) : base(address, opcodeField, 1) {
    }

    public override void Execute(InstructionExecutionHelper helper) {
        if (helper.State.CarryFlag) {
            helper.State.AL = 0;
        } else {
            helper.State.AL = 0xFF;
        }
        helper.MoveIpAndSetNextNode(this);
    }

    public override InstructionNode ToInstructionAst(AstBuilder builder) {
        return new InstructionNode(InstructionOperation.SALC);
    }
}