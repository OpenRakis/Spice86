namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

public class FnInit : CfgInstruction {

    public FnInit(SegmentedAddress address, InstructionField<ushort> opcodeField, List<InstructionPrefix> prefixes) : base(address, opcodeField, prefixes, 1) {
    }
    public override void Execute(InstructionExecutionHelper helper) {
        // Do nothing, no FPU emulation, but this is used to detect FPU support.
        helper.MoveIpToEndOfInstruction(this);
    }

    public override InstructionNode ToInstructionAst(AstBuilder builder) {
        return new InstructionNode(InstructionOperation.FNINIT);
    }
}