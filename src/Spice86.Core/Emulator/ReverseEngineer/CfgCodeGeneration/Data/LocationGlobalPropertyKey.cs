namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Data;

using Spice86.Core.Emulator.CPU.Registers;

/// <summary>A global property key for a proven segment value at a constant offset.</summary>
internal sealed record LocationGlobalPropertyKey(DataSegmentValue Segment, ushort Offset, int Width)
    : GlobalPropertyKey(Offset, Width);
