namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.CommonGrammar;
using Spice86.Shared.Emulator.Memory;

public class JmpFarImm : InstructionWithSegmentedAddressField {
    private readonly SegmentedAddress _targetAddress;

    public JmpFarImm(
        SegmentedAddress address,
        InstructionField<ushort> opcodeField,
        InstructionField<SegmentedAddress> segmentedAddressField) :
        base(address, opcodeField, segmentedAddressField) {
        _targetAddress = SegmentedAddressField.Value;
    }

    public override void Execute(InstructionExecutionHelper helper) {
        helper.JumpFar(this, _targetAddress.Segment, _targetAddress.Offset);
    }
    
    public override string ToAssemblyString(InstructionRendererHelper helper) {
        return helper.ToAssemblyString("jmp far", helper.ToHex(_targetAddress));
    }
}