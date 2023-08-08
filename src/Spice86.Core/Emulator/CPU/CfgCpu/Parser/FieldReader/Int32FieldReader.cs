namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.FieldReader;

using Spice86.Core.Emulator.Memory.Indexable;

public class Int32FieldReader : InstructionFieldReader<int> {
    public Int32FieldReader(IIndexable memory, InstructionReaderAddressSource addressSource) :
        base(memory, addressSource) {
    }

    protected override int FieldSize() {
        return 4;
    }

    public override int PeekValue() {
        return Memory.Int32[CurrentAddress];
    }
}