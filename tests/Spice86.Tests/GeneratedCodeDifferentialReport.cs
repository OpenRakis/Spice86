namespace Spice86.Tests;

using Spice86.Tests.AsmFixtures;

/// <summary>
/// Writes one line per compared fixture run to <c>generated-code/differential.txt</c>, reporting how the
/// final state of the emulated run and the final state of the generated run differ. The report is
/// informational: it never fails a test.
/// </summary>
internal static class GeneratedCodeDifferentialReport {
    /// <summary>Line written as the first line of the report.</summary>
    public const string Header = "fixture | registers | ram";

    private const int MaxWrittenRanges = 10;

    private static readonly object ReportFileLock = new();
    private static bool _reportFileInitialized;

    /// <summary>Describes the differences between two final states as a single line.</summary>
    /// <param name="goldenKey">Key of the fixture that produced both states.</param>
    /// <param name="emulated">Final state of the emulated run.</param>
    /// <param name="generated">Final state of the generated run.</param>
    /// <returns>One line describing the differences.</returns>
    public static string Describe(string goldenKey, AsmFinalState emulated, AsmFinalState generated) {
        string registers = DescribeRegisters(emulated, generated);
        string ram = DescribeRam(emulated.Ram, generated.Ram);
        if (registers == "none" && ram == "none") {
            return goldenKey + " | identical";
        }
        return goldenKey + " | registers: " + registers + " | ram: " + ram;
    }

    /// <summary>Appends one described line to the report, which starts with a header on the first write.</summary>
    /// <param name="goldenKey">Key of the fixture that produced both states.</param>
    /// <param name="emulated">Final state of the emulated run.</param>
    /// <param name="generated">Final state of the generated run.</param>
    public static void Append(string goldenKey, AsmFinalState emulated, AsmFinalState generated) {
        string outputDirectory = Path.Join(AppContext.BaseDirectory, "generated-code");
        Directory.CreateDirectory(outputDirectory);
        string reportPath = Path.Join(outputDirectory, "differential.txt");
        lock (ReportFileLock) {
            if (!_reportFileInitialized) {
                File.Delete(reportPath);
                File.AppendAllText(reportPath, Header + Environment.NewLine);
                _reportFileInitialized = true;
            }
            File.AppendAllText(reportPath, Describe(goldenKey, emulated, generated) + Environment.NewLine);
        }
    }

    private static string DescribeRegisters(AsmFinalState emulated, AsmFinalState generated) {
        List<string> differences = new();
        for (int i = 0; i < emulated.Registers.Count; i++) {
            string name = emulated.Registers[i].Name;
            ushort emulatedValue = emulated.Registers[i].Value;
            ushort generatedValue = generated.Registers[i].Value;
            if (emulatedValue != generatedValue) {
                differences.Add($"{name} 0x{emulatedValue:X4}->0x{generatedValue:X4}");
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
        if (differingBytes == 0) {
            return "none";
        }
        return $"{differingBytes} bytes in {ranges.Count} ranges: {DescribeRanges(ranges)}";
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
