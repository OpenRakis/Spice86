namespace Spice86.Tests.CpuTests.SingleStepTests;

/// <summary>
/// Tracks statistics for CPU test execution including per-opcode and global pass/fail/skip counts.
/// </summary>
public class TestStatistics {
    private const int MaxSamplesPerOpcode = 3;

    public TestCounters TotalCounters { get; } = new();
    public Dictionary<string, TestCounters> OpcodeStats { get; } = new();
    public List<string> FailingTestHashes { get; } = new();

    /// <summary>
    /// Per-opcode error reason distribution (e.g., "Flags differs" -> 42 occurrences).
    /// </summary>
    public Dictionary<string, Dictionary<string, int>> OpcodeErrorReasons { get; } = new();

    /// <summary>
    /// Sample error messages per opcode for investigation (capped at MaxSamplesPerOpcode).
    /// </summary>
    public Dictionary<string, List<string>> OpcodeSampleErrors { get; } = new();

    public void RecordPassingTest(string opcode) {
        EnsureOpcode(opcode);
        TotalCounters.Passing++;
        OpcodeStats[opcode].Passing++;
    }

    public void RecordFailingTest(string opcode, string hash, string errorMessage) {
        EnsureOpcode(opcode);
        TotalCounters.Failing++;
        OpcodeStats[opcode].Failing++;
        FailingTestHashes.Add(hash);

        string errorReason = ExtractErrorReason(errorMessage);
        Dictionary<string, int> reasons = OpcodeErrorReasons[opcode];
        reasons[errorReason] = reasons.GetValueOrDefault(errorReason) + 1;

        List<string> samples = OpcodeSampleErrors[opcode];
        if (samples.Count < MaxSamplesPerOpcode) {
            samples.Add(errorMessage);
        }
    }

    private void EnsureOpcode(string opcode) {
        if (!OpcodeStats.ContainsKey(opcode)) {
            OpcodeStats[opcode] = new TestCounters();
            OpcodeErrorReasons[opcode] = new Dictionary<string, int>();
            OpcodeSampleErrors[opcode] = new List<string>();
        }
    }

    /// <summary>
    /// Extracts a short categorized reason from the error message.
    /// Returns e.g. "Flags:Auxiliary differs" or "EAX mismatch" or "Memory@address".
    /// </summary>
    private static string ExtractErrorReason(string errorMessage) {
        // Find the "Error:" section and extract the core assertion message
        int errorIndex = errorMessage.IndexOf("Error:", StringComparison.Ordinal);
        string assertionMessage = errorIndex >= 0 ? errorMessage[(errorIndex + 6)..].Trim() : errorMessage.Trim();

        // "Expected and actual are not the same for register Flags. ..."
        if (assertionMessage.Contains("register Flags")) {
            // Extract which flags differ
            int dotIndex = assertionMessage.IndexOf(". ", StringComparison.Ordinal);
            if (dotIndex >= 0) {
                string flagsPart = assertionMessage[(dotIndex + 2)..];
                return $"Flags:{flagsPart.Trim()}";
            }
            return "Flags";
        }

        // "Expected and actual are not the same for register EAX."
        if (assertionMessage.Contains("register ")) {
            int regStart = assertionMessage.IndexOf("register ", StringComparison.Ordinal) + 9;
            int regEnd = assertionMessage.IndexOf('.', regStart);
            if (regEnd > regStart) {
                return assertionMessage[regStart..regEnd];
            }
        }

        // "Byte at address XXXXX differs."
        if (assertionMessage.Contains("Byte at address")) {
            return "Memory";
        }

        // Truncate for unknown patterns
        return assertionMessage.Length > 80 ? assertionMessage[..80] : assertionMessage;
    }
}
