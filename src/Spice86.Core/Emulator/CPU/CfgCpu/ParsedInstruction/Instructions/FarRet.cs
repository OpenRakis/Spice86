namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Shared.Emulator.Memory;

public class FarRet : CfgInstruction {

    public FarRet(SegmentedAddress address, InstructionField<ushort> opcodeField) : base(address, opcodeField) {
    }
    public override void Execute(InstructionExecutionHelper helper) {
        helper.HandleFarRet(this, 0);
    }
}