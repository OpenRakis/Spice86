namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

public class FnInit : CfgInstruction {

    public FnInit(SegmentedAddress address, InstructionField<ushort> opcodeField, List<InstructionPrefix> prefixes) : base(address, opcodeField, prefixes) {
    }
    public override void Execute(InstructionExecutionHelper helper) {
        // Do nothing, no FPU emulation, but this is used to detect FPU support.
        helper.MoveIpAndSetNextNode(this);
    }
}