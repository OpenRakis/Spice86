namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Undocumented instruction SALC
/// </summary>
public class Salc(SegmentedAddress address, InstructionField<ushort> opcodeField)
    : CfgInstruction(address, opcodeField, 1) {
    public override void Execute(InstructionExecutionHelper helper) {
        helper.State.AL = helper.State.CarryFlag ? (byte)0xFF : (byte)0;
        helper.MoveIpToEndOfInstruction(this);
    }

    public override InstructionNode ToInstructionAst(AstBuilder builder) {
        return new InstructionNode(InstructionOperation.SALC);
    }

    public override IVisitableAstNode GenerateExecutionAst(AstBuilder builder) {
        ValueNode al = builder.Register.Reg8(RegisterIndex.AxIndex);
        IfElseNode ternaryAssign = builder.ControlFlow.TernaryAssign(
            DataType.UINT8,
            al,
            builder.Flag.Carry(),
            builder.Constant.ToNode((byte)0xFF),
            builder.Constant.ToNode((byte)0));
        return builder.WithIpAdvancement(this, ternaryAssign);
    }
}
