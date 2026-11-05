namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Data;

using Spice86.Core.Emulator.CPU.Registers;

/// <summary>
/// A proven segment value of a location: absolute (<c>"0x2000"</c>) or relative to the configured
/// program entry segment (<c>"image+0x0010"</c> / <c>"image-0x0003"</c>) when the value sits inside
/// a DOS-loaded image. <see cref="Value"/> is the difference for a relative value, the segment
/// itself otherwise.
/// </summary>
internal readonly record struct DataSegmentValue(bool ImageRelative, int Value);
