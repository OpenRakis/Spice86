namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.FieldReader;

using Spice86.Core.Emulator.Memory.Indexable;
using Spice86.Shared.Emulator.Memory;

public class SegmentedAddress32InstructionFieldReader : InstructionFieldReader<SegmentedAddress32> {
    public SegmentedAddress32InstructionFieldReader(IIndexable memory, InstructionReaderAddressSource addressSource) :
        base(memory, addressSource) {
    }

    protected override int FieldSize() {
        return 6;
    }

    public override SegmentedAddress32 PeekValue() {
        uint offset = PeekUInt32(0);
        ushort segment = PeekUInt16(4);
        return new SegmentedAddress32(segment, offset);
    }
}