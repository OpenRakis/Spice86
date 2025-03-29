namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

public class PushF32 : CfgInstruction {
    public PushF32(SegmentedAddress address, InstructionField<ushort> opcodeField, List<InstructionPrefix> prefixes) :
        base(address, opcodeField, prefixes) {
    }

    public override void Execute(InstructionExecutionHelper helper) {
        helper.Stack.Push32(helper.State.Flags.FlagRegister & 0x00FCFFFF);
        helper.MoveIpAndSetNextNode(this);
    }

    public override string ToAssemblyString(InstructionRendererHelper helper) {
        return helper.ToAssemblyString("pushfd");
    }
}