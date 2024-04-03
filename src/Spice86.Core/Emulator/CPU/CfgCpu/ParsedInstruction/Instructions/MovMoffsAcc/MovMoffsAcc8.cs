namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.MovMoffsAcc;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

public class MovMoffsAcc8 : MovMoffsAcc {
    public MovMoffsAcc8(SegmentedAddress address, InstructionField<byte> opcodeField, List<InstructionPrefix> prefixes,
        uint segmentRegisterIndex, InstructionField<ushort> offsetField) : base(address, opcodeField, prefixes,
        segmentRegisterIndex, offsetField) {
    }

    public override void Visit(ICfgNodeVisitor visitor) {
        visitor.Accept(this);
    }
}