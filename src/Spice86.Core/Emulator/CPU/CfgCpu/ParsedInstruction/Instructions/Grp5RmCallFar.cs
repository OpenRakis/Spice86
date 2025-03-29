namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

public class Grp5RmCallFar : InstructionWithModRm {
    public Grp5RmCallFar(SegmentedAddress address, InstructionField<ushort> opcodeField,
        List<InstructionPrefix> prefixes, ModRmContext modRmContext) : base(address, opcodeField, prefixes,
        modRmContext) {
    }

    public override void Execute(InstructionExecutionHelper helper) {
        helper.ModRm.RefreshWithNewModRmContext(ModRmContext);
        uint ipAddress = helper.ModRm.MandatoryMemoryAddress;
        SegmentedAddress targetAddress = helper.Memory.SegmentedAddress[ipAddress];
        helper.FarCallWithReturnIpNextInstruction(this, targetAddress);
    }
}