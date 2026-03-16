namespace Spice86.Tests.CpuTests.SingleStepTests;

/// <summary>
/// Tracks statistics for CPU test execution including per-opcode and global pass/fail/skip counts.
/// </summary>
public class TestStatistics {
    public TestCounters TotalCounters { get; } = new();
    public Dictionary<string, TestCounters> OpcodeStats { get; } = new();
    public List<string> FailingTestHashes { get; } = new();

    public void RecordPassingTest(string opcode) {
        if (!OpcodeStats.ContainsKey(opcode)) {
            OpcodeStats[opcode] = new TestCounters();
        }

        TotalCounters.Passing++;
        OpcodeStats[opcode].Passing++;
    }

    public void RecordFailingTest(string opcode, string hash) {
        if (!OpcodeStats.ContainsKey(opcode)) {
            OpcodeStats[opcode] = new TestCounters();
        }

        TotalCounters.Failing++;
        OpcodeStats[opcode].Failing++;
        FailingTestHashes.Add(hash);
    }
}
