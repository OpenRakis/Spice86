namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Data;

using Spice86.Core.Emulator.CPU.Registers;

/// <summary>A global property key for an access through <paramref name="Segment"/> at a constant offset.</summary>
internal sealed record RegisterGlobalPropertyKey(SegmentRegisterIndex Segment, ushort Offset, int Width)
    : GlobalPropertyKey(Offset, Width) {
    public static RegisterGlobalPropertyKey Of(ConstantMemoryAccess access) =>
        new(access.Segment, access.Offset, access.Width);
}
