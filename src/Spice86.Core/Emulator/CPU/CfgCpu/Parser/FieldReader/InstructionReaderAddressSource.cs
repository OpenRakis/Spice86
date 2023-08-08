namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.FieldReader;

using Spice86.Shared.Emulator.Memory;

public class InstructionReaderAddressSource {
    private SegmentedAddress _instructionAddress;

    public SegmentedAddress InstructionAddress {
        get => _instructionAddress;
        set {
            _instructionAddress = value;
            IndexInInstruction = 0;
        }
    }

    public int IndexInInstruction { get; set; }

    public SegmentedAddress CurrentAddress => new SegmentedAddress(InstructionAddress.Segment,
        (ushort)(InstructionAddress.Offset + IndexInInstruction));

    public InstructionReaderAddressSource(SegmentedAddress instructionAddress) {
        _instructionAddress = instructionAddress;
    }
}