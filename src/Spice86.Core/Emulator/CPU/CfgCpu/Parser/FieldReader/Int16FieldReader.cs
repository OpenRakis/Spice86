namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.FieldReader;

using Spice86.Core.Emulator.Memory.Indexable;

public class Int16FieldReader : InstructionFieldReader<short> {
    public Int16FieldReader(IIndexable memory, InstructionReaderAddressSource addressSource) :
        base(memory, addressSource) {
    }

    protected override int FieldSize() {
        return 2;
    }

    public override short PeekValue() {
        return Memory.Int16[CurrentAddress];
    }
}