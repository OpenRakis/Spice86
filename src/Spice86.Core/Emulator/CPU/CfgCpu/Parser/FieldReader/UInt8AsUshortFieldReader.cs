namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.FieldReader;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.Memory.Indexable;

public class UInt8AsUshortFieldReader : InstructionFieldReader<ushort> {
    public UInt8AsUshortFieldReader(IIndexable memory, InstructionReaderAddressSource addressSource) :
        base(memory, addressSource) {
    }

    public override InstructionField<ushort> PeekField(bool finalValue) {
        if (!finalValue) {
            throw new ArgumentException("Can only peek final value for this type of field");
        }
        return base.PeekField(true);
    }

    protected override int FieldSize() {
        return 1;
    }

    public override ushort PeekValue() {
        return Memory.UInt8[CurrentAddress];
    }
}