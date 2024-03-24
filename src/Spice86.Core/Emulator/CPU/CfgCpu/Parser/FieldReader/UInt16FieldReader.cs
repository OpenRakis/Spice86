namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.FieldReader;

using Spice86.Core.Emulator.Memory.Indexable;

public class UInt16FieldReader : InstructionFieldReader<ushort> {
    public UInt16FieldReader(IIndexable memory, InstructionReaderAddressSource addressSource) :
        base(memory, addressSource) {
    }

    protected override int FieldSize() {
        return 2;
    }

    public override ushort PeekValue() {
        return Memory.UInt16[CurrentAddress];
    }
}