namespace Spice86.Tests.CpuTests.SingleStepTests;

/// <summary>
/// Represents registers and RAM states
/// </summary>
public class CpuTestState {
    public CpuRegisters Registers { get; set; } = new();
    public RamEntry[] Ram { get; set; } = Array.Empty<RamEntry>();
}
