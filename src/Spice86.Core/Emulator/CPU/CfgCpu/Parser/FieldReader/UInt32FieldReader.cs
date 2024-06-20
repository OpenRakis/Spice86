namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.FieldReader;

using Spice86.Core.Emulator.Memory.Indexable;

public class UInt32FieldReader : InstructionFieldReader<uint> {
    public UInt32FieldReader(IIndexable memory, InstructionReaderAddressSource addressSource) :
        base(memory, addressSource) {
    }

    protected override int FieldSize() {
        return 4;
    }

    public override uint PeekValue() {
        return Memory.UInt32[CurrentAddress];
    }
}