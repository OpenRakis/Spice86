namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.CommonGrammar;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

public class BswapReg32(
    SegmentedAddress address, InstructionField<ushort> opcodeField, List<InstructionPrefix> prefixes,
    int registerIndex)
    : InstructionWithRegisterIndex(address, opcodeField, prefixes, registerIndex, 1) {
    public override void Execute(InstructionExecutionHelper helper) {
        uint v = helper.UInt32Registers[RegisterIndex];
        helper.UInt32Registers[RegisterIndex] = (v >> 24)
                                                | ((v >> 8) & 0x0000FF00u)
                                                | ((v << 8) & 0x00FF0000u)
                                                | (v << 24);
        helper.MoveIpToEndOfInstruction(this);
    }

    public override InstructionNode ToInstructionAst(AstBuilder builder) {
        return new InstructionNode(InstructionOperation.BSWAP,
            builder.Register.Reg(builder.UType(32), RegisterIndex));
    }
}