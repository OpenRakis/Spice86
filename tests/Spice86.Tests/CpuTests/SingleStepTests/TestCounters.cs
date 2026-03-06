namespace Spice86.Tests.CpuTests.SingleStepTests;

/// <summary>
/// Counters for test results tracking passing, failing, and skipped tests.
/// </summary>
public class TestCounters {
    public int Passing { get; set; }
    public int Failing { get; set; }

    public int Total => Passing + Failing;
}
