namespace Spice86.Tests.CpuTests.SingleStepTests;

/// <summary>
/// Represents a RAM value at a given address
/// </summary>
/// <param name="Address"></param>
/// <param name="Value"></param>
public record RamEntry(uint Address, byte Value);
