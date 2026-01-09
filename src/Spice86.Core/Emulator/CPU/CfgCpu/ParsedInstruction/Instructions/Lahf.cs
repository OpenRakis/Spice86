namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Shared.Emulator.Memory;

public class Lahf : CfgInstruction {
    public Lahf(SegmentedAddress address, InstructionField<ushort> opcodeField) :
        base(address, opcodeField, 1) {
    }

    public override void Execute(InstructionExecutionHelper helper) {
        helper.State.AH = (byte)helper.State.Flags.FlagRegister;
        helper.MoveIpToEndOfInstruction(this);
    }

    public override InstructionNode ToInstructionAst(AstBuilder builder) {
        return new InstructionNode(InstructionOperation.LAHF);
    }
}