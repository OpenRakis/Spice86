namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
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
}
