namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Shared.Emulator.Memory;

public class Nop : CfgInstruction {
    public Nop(SegmentedAddress address, InstructionField<byte> opcodeField) : base(address, opcodeField) {
    }

    public override void Execute(InstructionExecutionHelper helper) {
        // Well nothing to do :)
        helper.MoveIpAndSetNextNode(this);
    }
}