namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

public class Fnstcw : InstructionWithModRm {

    public Fnstcw(SegmentedAddress address, InstructionField<ushort> opcodeField, List<InstructionPrefix> prefixes, ModRmContext modRmContext) : base(address, opcodeField, prefixes, modRmContext, 1) {
    }

    public override InstructionNode ToInstructionAst(AstBuilder builder) {
        return new InstructionNode(InstructionOperation.FNSTCW);
    }

    protected override IVisitableAstNode BuildExecutionAst(AstBuilder builder) {
        ValueNode rmNode = builder.ModRm.RmToNode(DataType.UINT16, ModRmContext);
        ValueNode controlWordValue = builder.Constant.ToNode((ushort)0x37F);
        return builder.WithIpAdvancement(this, builder.Assign(DataType.UINT16, rmNode, controlWordValue));
    }
}