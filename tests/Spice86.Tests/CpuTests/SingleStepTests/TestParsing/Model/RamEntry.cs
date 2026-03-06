namespace Spice86.Tests.CpuTests.SingleStepTests.TestParsing.Model;

/// <summary>
/// Represents a single RAM entry with an address and byte value
/// </summary>
public record RamEntry(uint Address, byte Value);