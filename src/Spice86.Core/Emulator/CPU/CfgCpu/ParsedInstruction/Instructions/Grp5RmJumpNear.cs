namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Interfaces;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;

public class Grp5RmJumpNear : InstructionWithModRm, IJumpInstruction {
    public Grp5RmJumpNear(SegmentedAddress address, InstructionField<ushort> opcodeField,
        List<InstructionPrefix> prefixes, ModRmContext modRmContext) : base(address, opcodeField, prefixes,
        modRmContext, null) {
    }

    public override InstructionNode ToInstructionAst(AstBuilder builder) {
        return new InstructionNode(InstructionOperation.JMP_NEAR, builder.ModRm.RmToNode(DataType.UINT16, ModRmContext));
    }

    public override void Execute(InstructionExecutionHelper helper) {
        helper.ModRm.RefreshWithNewModRmContext(ModRmContext);
        ushort ip = helper.ModRm.RM16;
        helper.JumpNear(this, ip);
    }

    protected override IVisitableAstNode BuildExecutionAst(AstBuilder builder) {
        return new JumpNearNode(this, builder.ModRm.RmToNode(DataType.UINT16, ModRmContext));
    }
}