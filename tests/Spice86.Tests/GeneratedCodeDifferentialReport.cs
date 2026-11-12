namespace Spice86.Tests;

using FluentAssertions;

using Spice86.Tests.AsmFixtures;

/// <summary>
/// Writes one line per compared fixture run to <c>generated-code/differential.txt</c>, reporting how the
/// final state of the emulated run and the final state of the generated run differ. The strict
/// <see cref="Check"/> gate turns any difference into a failure; the report stays informational.
/// </summary>
internal static class GeneratedCodeDifferentialReport {
    /// <summary>The generation policy recorded for compared runs: interpreted and generated final
    /// states must be identical.</summary>
    public const string StrictPolicy = "strict";

    /// <summary>Line written as the first line of the report.</summary>
    public const string Header = "fixture | policy | registers | ram | extended";

    private const int MaxWrittenRanges = 10;

    private static readonly object ReportFileLock = new();
    private static bool _reportFileInitialized;

    /// <summary>Describes the differences between two final states as a single line.</summary>
    /// <param name="goldenKey">Key of the fixture that produced both states.</param>
    /// <param name="policy">The generation policy the comparison ran under.</param>
    /// <param name="emulated">Final state of the emulated run.</param>
    /// <param name="generated">Final state of the generated run.</param>
    /// <returns>One line describing the differences.</returns>
    public static string Describe(string goldenKey, string policy, AsmFinalState emulated,
        AsmFinalState generated) {
        string registers = DescribeRegisters(emulated, generated);
        string ram = DescribeRam(emulated.Ram, generated.Ram);
        string extended = DescribeExtended(emulated, generated);
        if (registers == "none" && ram == "none" && extended == "none") {
            return $"{goldenKey} | {policy} | identical";
        }
        return $"{goldenKey} | {policy} | registers: {registers} | ram: {ram} | extended: {extended}";
    }

    /// <summary>Appends one described line to the report, which starts with a header on the first write.</summary>
    /// <param name="goldenKey">Key of the fixture that produced both states.</param>
    /// <param name="policy">The generation policy the comparison ran under.</param>
    /// <param name="emulated">Final state of the emulated run.</param>
    /// <param name="generated">Final state of the generated run.</param>
    public static void Append(string goldenKey, string policy, AsmFinalState emulated,
        AsmFinalState generated) {
        AppendLine(Describe(goldenKey, policy, emulated, generated));
    }

    /// <summary>Appends the comparison of <paramref name="emulated"/> and <paramref name="generated"/>
    /// to the report and fails when their strict final states differ.</summary>
    /// <param name="goldenKey">Key of the fixture that produced both states.</param>
    /// <param name="emulated">Final state of the interpreted run.</param>
    /// <param name="generated">Final state of the generated run.</param>
    /// <summary>Appends the comparison of <paramref name="emulated"/> and <paramref name="generated"/>
    /// to the report and fails when their strict final states differ, under the strict policy.</summary>
    /// <param name="goldenKey">Key of the fixture that produced both states.</param>
    /// <param name="emulated">Final state of the interpreted run.</param>
    /// <param name="generated">Final state of the generated run.</param>
    public static void Check(string goldenKey, AsmFinalState emulated, AsmFinalState generated) =>
        Check(goldenKey, StrictPolicy, emulated, generated);

    /// <summary>Appends the comparison of <paramref name="emulated"/> and <paramref name="generated"/>
    /// to the report and fails when their strict final states differ.</summary>
    /// <param name="goldenKey">Key of the fixture that produced both states.</param>
    /// <param name="policy">The generation policy the comparison ran under.</param>
    /// <param name="emulated">Final state of the interpreted run.</param>
    /// <param name="generated">Final state of the generated run.</param>
    public static void Check(string goldenKey, string policy, AsmFinalState emulated,
        AsmFinalState generated) {
        string line = Describe(goldenKey, policy, emulated, generated);
        AppendLine(line);
        line.Should().EndWith("| identical", "the generated run must end in the interpreted run's " +
            "final state: {0}", line);
    }

    /// <summary>Appends a line for a scenario whose generated execution is skipped.</summary>
    /// <param name="goldenKey">Key of the fixture.</param>
    /// <param name="reason">Why the generated run is not performed.</param>
    public static void AppendSkipped(string goldenKey, string reason) {
        reason.Should().NotBeNullOrEmpty("a scenario skipped in generated mode needs a reason");
        AppendLine($"{goldenKey} | skipped | {reason}");
    }

    private static void AppendLine(string line) {
        string outputDirectory = Path.Join(AppContext.BaseDirectory, "generated-code");
        Directory.CreateDirectory(outputDirectory);
        string reportPath = Path.Join(outputDirectory, "differential.txt");
        lock (ReportFileLock) {
            if (!_reportFileInitialized) {
                File.Delete(reportPath);
                File.AppendAllText(reportPath, Header + Environment.NewLine);
                _reportFileInitialized = true;
            }
            File.AppendAllText(reportPath, line + Environment.NewLine);
        }
    }

    private static string DescribeRegisters(AsmFinalState emulated, AsmFinalState generated) {
        List<string> differences = new();
        for (int i = 0; i < emulated.Registers.Count; i++) {
            string name = emulated.Registers[i].Name;
            uint emulatedValue = emulated.Registers[i].Value;
            uint generatedValue = generated.Registers[i].Value;
            if (emulatedValue != generatedValue) {
                differences.Add($"{name} 0x{emulatedValue:X8}->0x{generatedValue:X8}");
            }
        }
        if (differences.Count == 0) {
            return "none";
        }
        return string.Join(", ", differences);
    }

    private static string DescribeRam(byte[] emulated, byte[] generated) {
        int length = Math.Min(emulated.Length, generated.Length);
        int differingBytes = 0;
        List<string> ranges = new();
        int rangeStart = -1;
        for (int i = 0; i < length; i++) {
            if (emulated[i] == generated[i]) {
                if (rangeStart >= 0) {
                    ranges.Add(FormatRange(rangeStart, i - 1));
                    rangeStart = -1;
                }
                continue;
            }
            differingBytes++;
            if (rangeStart < 0) {
                rangeStart = i;
            }
        }
        if (rangeStart >= 0) {
            ranges.Add(FormatRange(rangeStart, length - 1));
        }
        if (emulated.Length != generated.Length) {
            ranges.Add($"length 0x{emulated.Length:X}->0x{generated.Length:X}");
        }
        if (differingBytes == 0 && ranges.Count == 0) {
            return "none";
        }
        return $"{differingBytes} bytes in {ranges.Count} ranges: {DescribeRanges(ranges)}";
    }

    private static string DescribeExtended(AsmFinalState emulated, AsmFinalState generated) {
        List<string> parts = new();
        Dictionary<string, byte[]> generatedRegions =
            generated.ExtendedRegions.ToDictionary(region => region.Name, region => region.Bytes);
        HashSet<string> seen = new();
        foreach ((string name, byte[] emulatedBytes) in emulated.ExtendedRegions) {
            seen.Add(name);
            string differences = generatedRegions.TryGetValue(name, out byte[]? generatedBytes)
                ? DescribeRam(emulatedBytes, generatedBytes)
                : "missing";
            if (differences != "none") {
                parts.Add($"{name}: {differences}");
            }
        }
        foreach ((string name, _) in generated.ExtendedRegions) {
            if (!seen.Contains(name)) {
                parts.Add($"{name}: unexpected");
            }
        }
        return parts.Count == 0 ? "none" : string.Join("; ", parts);
    }

    private static string DescribeRanges(List<string> ranges) {
        List<string> written = new();
        for (int i = 0; i < ranges.Count && i < MaxWrittenRanges; i++) {
            written.Add(ranges[i]);
        }
        string line = string.Join(", ", written);
        if (ranges.Count > MaxWrittenRanges) {
            line += ", ...";
        }
        return line;
    }

    private static string FormatRange(int start, int end) {
        return $"0x{start:X5}-0x{end:X5}";
    }
}
