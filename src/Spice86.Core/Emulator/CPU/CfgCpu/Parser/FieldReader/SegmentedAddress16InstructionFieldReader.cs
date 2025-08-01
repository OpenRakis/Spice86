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
        return Memory.SegmentedAddress16[CurrentAddress];
    }
}