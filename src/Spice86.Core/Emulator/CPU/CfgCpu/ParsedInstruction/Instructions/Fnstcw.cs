namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

public class Fnstcw : InstructionWithModRm {

    public Fnstcw(SegmentedAddress address, InstructionField<ushort> opcodeField, List<InstructionPrefix> prefixes, ModRmContext modRmContext) : base(address, opcodeField, prefixes, modRmContext) {
    }
    public override void Execute(InstructionExecutionHelper helper) {
        helper.ModRm.RefreshWithNewModRmContext(ModRmContext);
        // Set the control word to the value expected after init since FPU is not supported.
        helper.ModRm.RM16 = 0x37F;
        helper.MoveIpAndSetNextNode(this);
    }

    public override string ToAssemblyString(InstructionRendererHelper helper) {
        return helper.ToAssemblyString("fnstcw");
    }
}