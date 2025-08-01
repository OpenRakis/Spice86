namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.FieldReader;

using Spice86.Core.Emulator.Memory.Indexable;
using Spice86.Shared.Emulator.Memory;

public class SegmentedAddress32InstructionFieldReader : InstructionFieldReader<SegmentedAddress> {
    public SegmentedAddress32InstructionFieldReader(IIndexable memory, InstructionReaderAddressSource addressSource) :
        base(memory, addressSource) {
    }

    protected override int FieldSize() {
        return 6;
    }

    public override SegmentedAddress PeekValue() {
        // We still read a segmented address since in real mode we ignore the last 2 bytes
        return Memory.SegmentedAddress32[CurrentAddress];
    }
}