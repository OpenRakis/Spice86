namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

public class Cbw16 : CfgInstruction {
    public Cbw16(SegmentedAddress address, InstructionField<ushort> opcodeField, List<InstructionPrefix> prefixes) :
        base(address, opcodeField, prefixes, 1) {
    }

    public override void Execute(InstructionExecutionHelper helper) {
        // CBW, Convert byte to word
        short shortValue = (sbyte)helper.State.AL;
        helper.State.AX = (ushort)shortValue;
        helper.MoveIpAndSetNextNode(this);
    }

    public override InstructionNode ToInstructionAst(AstBuilder builder) {
        return new InstructionNode(InstructionOperation.CBW);
    }
}