namespace Spice86.Tests.CpuTests.SingleStepTests.TestParsing.Model;

/// <summary>
/// Represents the CPU state (registers and RAM)
/// </summary>
public class CpuState {
    public CpuRegisters Registers { get; set; } = new();
    public RamEntry[] Ram { get; set; } = Array.Empty<RamEntry>();
}