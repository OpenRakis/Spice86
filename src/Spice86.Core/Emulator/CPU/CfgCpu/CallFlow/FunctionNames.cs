namespace Spice86.Core.Emulator.CPU.CfgCpu.CallFlow;

using Spice86.Shared.Emulator.Memory;

public class FunctionNames {
    public Dictionary<SegmentedAddress, string> Names { get; } = new();
}