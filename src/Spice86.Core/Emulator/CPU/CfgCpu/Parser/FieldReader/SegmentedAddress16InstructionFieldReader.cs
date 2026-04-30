namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.FieldReader;

using Spice86.Core.Emulator.Memory.Indexable;
using Spice86.Shared.Emulator.Memory;

public class SegmentedAddress16InstructionFieldReader : InstructionFieldReader<SegmentedAddress> {
    public SegmentedAddress16InstructionFieldReader(IIndexable memory, InstructionReaderAddressSource addressSource) :
        base(memory, addressSource) {
    }

    protected override int FieldSize() {
        return 4;
    }

    public override SegmentedAddress PeekValue() {
        ushort offset = PeekUInt16(0);
        ushort segment = PeekUInt16(2);
        return new SegmentedAddress(segment, offset);
    }
}