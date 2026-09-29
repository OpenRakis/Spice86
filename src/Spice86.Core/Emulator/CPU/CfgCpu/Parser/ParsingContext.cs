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

    /// <summary>Returns <see cref="BitWidth.DWORD_32"/> when operand-size prefix is active, <see cref="BitWidth.WORD_16"/> otherwise.</summary>
    public BitWidth DefaultWordOperandBitWidth { get; }

    /// <param name="address">The address of the instruction being parsed.</param>
    /// <param name="opcodeField">The instruction's opcode field.</param>
    /// <param name="prefixes">The instruction's decoded prefixes.</param>
    /// <param name="codeSegmentDefaultBig">
    /// The CURRENT code segment's D/B bit (32-bit default operand/address size). Real mode and 16-bit
    /// protected-mode segments pass <c>false</c>. The 0x66/0x67 prefixes TOGGLE relative to this default
    /// rather than unconditionally selecting 32-bit, matching real hardware: in a 32-bit-default segment,
    /// 0x66 present means 16-bit, and 0x66 absent means 32-bit.
    /// </param>
    public ParsingContext(SegmentedAddress address, InstructionField<ushort> opcodeField,
        List<InstructionPrefix> prefixes, bool codeSegmentDefaultBig) {
        Address = address;
        OpcodeField = opcodeField;
        Prefixes = prefixes;
        AddressWidthFromPrefixes = ComputeAddressSize(prefixes, codeSegmentDefaultBig);
        SegmentOverrideFromPrefixes = ComputeSegmentOverrideIndex(prefixes);
        HasOperandSize32 = ComputeHasOperandSize32(prefixes, codeSegmentDefaultBig);
        DefaultWordOperandBitWidth = HasOperandSize32 ? BitWidth.DWORD_32 : BitWidth.WORD_16;
    }

    private static int? ComputeSegmentOverrideIndex(List<InstructionPrefix> prefixes) {
        SegmentOverrideInstructionPrefix? overridePrefix =
            prefixes.OfType<SegmentOverrideInstructionPrefix>().LastOrDefault();
        return overridePrefix?.SegmentRegisterIndexValue;
    }

    private static BitWidth ComputeAddressSize(List<InstructionPrefix> prefixes, bool codeSegmentDefaultBig) {
        bool addressSize32PrefixPresent = prefixes.OfType<AddressSize32Prefix>().Any();
        return (codeSegmentDefaultBig ^ addressSize32PrefixPresent) ? BitWidth.DWORD_32 : BitWidth.WORD_16;
    }

    private static bool ComputeHasOperandSize32(IList<InstructionPrefix> prefixes, bool codeSegmentDefaultBig) {
        bool operandSize32PrefixPresent = prefixes.Any(p => p is OperandSize32Prefix);
        return codeSegmentDefaultBig ^ operandSize32PrefixPresent;
    }
}
