namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.PushReg;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

public class PushReg32 : InstructionWithRegisterIndex {
    public PushReg32(SegmentedAddress address, InstructionField<byte> opcodeField, List<InstructionPrefix> prefixes,
        int registerIndex) : base(address, opcodeField, prefixes, registerIndex) {
    }

    public override void Visit(ICfgNodeVisitor visitor) {
        visitor.Accept(this);
    }
}