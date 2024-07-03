namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Shared.Emulator.Memory;

public class NearRet : CfgInstruction {

    public NearRet(SegmentedAddress address, InstructionField<ushort> opcodeField) : base(address, opcodeField) {
    }
    public override void Execute(InstructionExecutionHelper helper) {
        helper.HandleNearRet(this, 0);
    }
}