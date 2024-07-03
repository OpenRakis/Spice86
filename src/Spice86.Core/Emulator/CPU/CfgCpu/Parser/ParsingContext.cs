namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

using System.Linq;
using System.Numerics;

public class ParsingContext : ModRmParsingContext {
    public SegmentedAddress Address { get; }
    public InstructionField<ushort> OpcodeField { get; }
    public List<InstructionPrefix> Prefixes { get; }
    public BitWidth AddressWidthFromPrefixes { get; }
    public int? SegmentOverrideFromPrefixes { get; }
    public bool HasOperandSize32 { get; }

    public ParsingContext(SegmentedAddress address, InstructionField<ushort> opcodeField,
        List<InstructionPrefix> prefixes) {
        Address = address;
        OpcodeField = opcodeField;
        Prefixes = prefixes;
        AddressWidthFromPrefixes = ComputeAddressSize(prefixes);
        SegmentOverrideFromPrefixes = ComputeSegmentOverrideIndex(prefixes);
        HasOperandSize32 = ComputeHasOperandSize32(prefixes);
    }

    private int? ComputeSegmentOverrideIndex(List<InstructionPrefix> prefixes) {
        SegmentOverrideInstructionPrefix? overridePrefix =
            prefixes.OfType<SegmentOverrideInstructionPrefix>().FirstOrDefault();
        return overridePrefix?.SegmentRegisterIndexValue;
    }

    private BitWidth ComputeAddressSize(List<InstructionPrefix> prefixes) {
        AddressSize32Prefix? addressSize32Prefix = prefixes.OfType<AddressSize32Prefix>().FirstOrDefault();
        return addressSize32Prefix == null ? BitWidth.WORD_16 : BitWidth.DWORD_32;
    }

    private bool ComputeHasOperandSize32(IList<InstructionPrefix> prefixes) {
        return prefixes.Where(p => p is OperandSize32Prefix).Any();
    }
}