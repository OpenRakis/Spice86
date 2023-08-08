namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.Parser.FieldReader;
using Spice86.Core.Emulator.Memory.Indexable;

public class InstructionReader {
    public InstructionReader(IIndexable memory) {
        InstructionReaderAddressSource = new InstructionReaderAddressSource(new(0, 0));
        Int8 = new(memory, InstructionReaderAddressSource);
        UInt8 = new(memory, InstructionReaderAddressSource);
        Int16 = new(memory, InstructionReaderAddressSource);
        UInt16 = new(memory, InstructionReaderAddressSource);
        Int32 = new(memory, InstructionReaderAddressSource);
        UInt32 = new(memory, InstructionReaderAddressSource);
        SegmentedAddress = new(memory, InstructionReaderAddressSource);
    }

    public InstructionReaderAddressSource InstructionReaderAddressSource { get; }
    public Int8FieldReader Int8 { get; }
    public UInt8FieldReader UInt8 { get; }
    public Int16FieldReader Int16 { get; }
    public UInt16FieldReader UInt16 { get; }
    public Int32FieldReader Int32 { get; }
    public UInt32FieldReader UInt32 { get; }
    public SegmentedAddressInstructionFieldReader SegmentedAddress { get; }
}