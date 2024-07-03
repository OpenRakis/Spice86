namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;

using Spice86.Shared.Emulator.Memory;

public interface ModRmParsingContext {
    public BitWidth AddressWidthFromPrefixes { get; }
    public int? SegmentOverrideFromPrefixes { get; }
}