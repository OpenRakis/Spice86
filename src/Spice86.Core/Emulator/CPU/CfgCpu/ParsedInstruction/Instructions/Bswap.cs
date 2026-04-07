namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.CommonGrammar;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

public class BswapReg32(
    SegmentedAddress address, InstructionField<ushort> opcodeField, List<InstructionPrefix> prefixes,
    int registerIndex)
    : InstructionWithRegisterIndex(address, opcodeField, prefixes, registerIndex, 1) {
    public override InstructionNode ToInstructionAst(AstBuilder builder) {
        return new InstructionNode(InstructionOperation.BSWAP,
            builder.Register.Reg(builder.UType(32), RegisterIndex));
    }

    protected override IVisitableAstNode BuildExecutionAst(AstBuilder builder) {
        ValueNode reg = builder.Register.Reg(DataType.UINT32, RegisterIndex);
        ValueNode swapped = builder.Bitwise.ByteSwap(reg);
        return builder.WithIpAdvancement(this, builder.Assign(DataType.UINT32, reg, swapped));
    }
}