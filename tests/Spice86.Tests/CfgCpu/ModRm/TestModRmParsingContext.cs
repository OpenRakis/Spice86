namespace Spice86.Tests.CfgCpu.ModRm;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Implementation of <see cref="ModRmParsingContext"/> for tests.
/// </summary>
public class TestModRmParsingContext : ModRmParsingContext {
    public TestModRmParsingContext(BitWidth addressWidthFromPrefixes, int? segmentOverrideFromPrefixes) {
        AddressWidthFromPrefixes = addressWidthFromPrefixes;
        SegmentOverrideFromPrefixes = segmentOverrideFromPrefixes;
    }
    public TestModRmParsingContext(BitWidth addressWidthFromPrefixes) : this(addressWidthFromPrefixes, null) {
    }
    public BitWidth AddressWidthFromPrefixes { get; }
    public int? SegmentOverrideFromPrefixes { get; }
}