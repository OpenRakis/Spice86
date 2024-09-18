namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Shared.Emulator.Memory;

public class InterruptOverflow : CfgInstruction {
    public InterruptOverflow(SegmentedAddress address, InstructionField<ushort> opcodeField) : base(address, opcodeField) {
    }

    public override void Execute(InstructionExecutionHelper helper) {
        if (helper.State.OverflowFlag) {
            helper.HandleInterruptInstruction(this, 4);
        } else {
            helper.MoveIpAndSetNextNode(this);
        }
    }
}