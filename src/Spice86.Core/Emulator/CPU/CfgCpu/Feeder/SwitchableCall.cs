namespace Spice86.Core.Emulator.CPU.CfgCpu.Feeder;

using Spice86.Shared.Emulator.Memory;

/// <summary>A switchable memory-indirect call: the instruction address and the target its pointer
/// currently holds in memory.</summary>
/// <param name="CallSite">Address of the call instruction.</param>
/// <param name="Target">Target the call's pointer currently holds.</param>
public readonly record struct SwitchableCall(SegmentedAddress CallSite, SegmentedAddress Target);
