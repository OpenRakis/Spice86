namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.CommonGrammar;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

public class CallNearImm : InstructionWithOffsetField<short> {
    private readonly ushort _targetIp;
    public CallNearImm(SegmentedAddress address, InstructionField<ushort> opcodeField,
        List<InstructionPrefix> prefixes, InstructionField<short> offsetField) : base(address, opcodeField, prefixes,
        offsetField) {
        _targetIp = (ushort)(NextInMemoryAddress.Offset + offsetField.Value);
    }

    public override void Execute(InstructionExecutionHelper helper) {
        helper.NearCallWithReturnIpNextInstruction(this, _targetIp);
    }
    
    public override string ToAssemblyString(InstructionRendererHelper helper) {
        return helper.ToAssemblyString("call near", helper.ToHex(_targetIp));
    }
}