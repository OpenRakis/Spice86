namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

public class Fnstsw : InstructionWithModRm {

    public Fnstsw(SegmentedAddress address, InstructionField<ushort> opcodeField, List<InstructionPrefix> prefixes, ModRmContext modRmContext) : base(address, opcodeField, prefixes, modRmContext, 1) {
    }
    public override void Execute(InstructionExecutionHelper helper) {
        helper.ModRm.RefreshWithNewModRmContext(ModRmContext);
        // Set non zero, means no FPU installed when called after FNINIT.
        helper.ModRm.RM16 = 0xFF;
        helper.MoveIpToEndOfInstruction(this);
    }

    public override InstructionNode ToInstructionAst(AstBuilder builder) {
        return new InstructionNode(InstructionOperation.FNSTSW);
    }

    public override IVisitableAstNode GenerateExecutionAst(AstBuilder builder) {
        ValueNode rmNode = builder.ModRm.RmToNode(DataType.UINT16, ModRmContext);
        ValueNode statusWordValue = builder.Constant.ToNode((ushort)0xFF);
        return builder.WithIpAdvancement(this, builder.Assign(DataType.UINT16, rmNode, statusWordValue));
    }
}