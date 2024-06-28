namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.FieldReader;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.Memory.Indexable;

public class UInt16BigEndianFieldReader : InstructionFieldReader<ushort> {
    public UInt16BigEndianFieldReader(IIndexable memory, InstructionReaderAddressSource addressSource) :
        base(memory, addressSource) {
    }

    public override InstructionField<ushort> PeekField(bool finalValue) {
        if (!finalValue) {
            throw new ArgumentException("Can only peek final value for this type of field");
        }
        return base.PeekField(true);
    }

    protected override int FieldSize() {
        return 2;
    }

    public override ushort PeekValue() {
        return Memory.UInt16BigEndian[CurrentAddress];
    }
}