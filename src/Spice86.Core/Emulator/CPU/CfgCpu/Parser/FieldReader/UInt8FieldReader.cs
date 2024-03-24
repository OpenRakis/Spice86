namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.FieldReader;

using Spice86.Core.Emulator.Memory.Indexable;

public class UInt8FieldReader : InstructionFieldReader<byte> {
    public UInt8FieldReader(IIndexable memory, InstructionReaderAddressSource addressSource) :
        base(memory, addressSource) {
    }

    protected override int FieldSize() {
        return 1;
    }

    public override byte PeekValue() {
        return Memory.UInt8[CurrentAddress];
    }
}