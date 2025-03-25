namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Interfaces;
using Spice86.Shared.Emulator.Memory;

public class FarRet : CfgInstruction, IReturnInstruction {

    public FarRet(SegmentedAddress address, InstructionField<ushort> opcodeField) : base(address, opcodeField) {
    }

    public CfgInstruction? CurrentCorrespondingCallInstruction { get; set; }

    public override void Execute(InstructionExecutionHelper helper) {
        helper.HandleFarRet(this, 0);
    }
    
    public override string ToAssemblyString(InstructionRendererHelper helper) {
        return "ret far";
    }
}