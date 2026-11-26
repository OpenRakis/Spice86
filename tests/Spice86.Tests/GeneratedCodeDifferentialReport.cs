namespace Spice86.Tests;

using FluentAssertions;

using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
using Spice86.Shared.Emulator.Memory;
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
    public const string Header = "fixture | policy | ram | extended";

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
        string ram = DescribeRam(emulated.Ram, generated.Ram);
        string extended = DescribeExtended(emulated, generated);
        if (ram == "none" && extended == "none") {
            return $"{goldenKey} | {policy} | identical";
        }
        return $"{goldenKey} | {policy} | ram: {ram} | extended: {extended}";
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
        Check(goldenKey, GenerationPolicy.Strict, emulated, generated);

    /// <summary>Appends the comparison of <paramref name="emulated"/> and <paramref name="generated"/>
    /// to the report and fails when their compared final states differ. Final registers are not
    /// compared: a program-ending <c>hlt</c> observes memory only.</summary>
    /// <param name="goldenKey">Key of the fixture that produced both states.</param>
    /// <param name="policy">The generation policy the comparison ran under.</param>
    /// <param name="emulated">Final state of the interpreted run.</param>
    /// <param name="generated">Final state of the generated run.</param>
    public static void Check(string goldenKey, GenerationPolicy policy, AsmFinalState emulated,
        AsmFinalState generated) =>
        Check(goldenKey, policy, emulated, generated, new HashSet<SegmentedAddress>(), [], []);

    /// <summary>Appends the comparison of <paramref name="emulated"/> and <paramref name="generated"/>
    /// to the report and fails when their compared final states differ, excluding from the RAM
    /// comparison only the six bytes below the recorded caller stack pointer of every software
    /// interrupt whose address is a generated frameless service call site: those are exactly the
    /// bytes the typed call's omitted interrupt frame would have occupied. Extended
    /// regions and every other RAM byte still decide.</summary>
    /// <param name="goldenKey">Key of the fixture that produced both states.</param>
    /// <param name="policy">The generation policy the comparison ran under.</param>
    /// <param name="emulated">Final state of the interpreted run.</param>
    /// <param name="generated">Final state of the generated run.</param>
    /// <param name="framelessServiceCallSites">The generated program's
    /// <c>FramelessServiceCallSites</c>.</param>
    public static void Check(string goldenKey, GenerationPolicy policy, AsmFinalState emulated,
        AsmFinalState generated, IReadOnlySet<SegmentedAddress> framelessServiceCallSites) =>
        Check(goldenKey, policy, emulated, generated, framelessServiceCallSites, [], []);

    /// <summary>Appends the comparison of <paramref name="emulated"/> and <paramref name="generated"/>
    /// to the report and fails when their compared final states differ. Two exclusion shapes apply:
    /// the six bytes below the recorded caller stack pointer of every software interrupt whose
    /// address is a generated frameless service call site, and - only when
    /// <paramref name="policy"/> enables the abandoned-stack-slots relaxation - the reserved
    /// argument bytes of every executed call whose address is a generated
    /// <c>CallWithStackSpace</c> site, starting at the stack pointer the interpreted run had when
    /// it reached that call. Those are exactly the bytes the typed call's omitted frame or
    /// argument writes would have occupied. Extended regions and every other RAM byte still
    /// decide.</summary>
    /// <param name="goldenKey">Key of the fixture that produced both states.</param>
    /// <param name="policy">The generation policy the comparison ran under.</param>
    /// <param name="emulated">Final state of the interpreted run.</param>
    /// <param name="generated">Final state of the generated run.</param>
    /// <param name="framelessServiceCallSites">The generated program's
    /// <c>FramelessServiceCallSites</c>.</param>
    /// <param name="stackSpaceCallSites">The generated program's <c>StackSpaceCallSites</c>: call
    /// instruction addresses with the argument bytes each reserved.</param>
    /// <param name="droppedStackSlotSites">The generated program's <c>DroppedStackSlotSites</c>:
    /// push instruction addresses with the byte count each dropped save pair omitted.</param>
    public static void Check(string goldenKey, GenerationPolicy policy, AsmFinalState emulated,
        AsmFinalState generated, IReadOnlySet<SegmentedAddress> framelessServiceCallSites,
        IReadOnlyList<(SegmentedAddress Address, int ArgumentBytes)> stackSpaceCallSites,
        IReadOnlyList<(SegmentedAddress Address, int Bytes)> droppedStackSlotSites) {
        List<(int Start, int Length)> excluded = [];
        foreach ((SegmentedAddress address, SegmentedAddress stackPointer) in emulated.SoftwareInterrupts) {
            if (!framelessServiceCallSites.Contains(address) || stackPointer.Linear < 6
                || stackPointer.Linear > emulated.Ram.Length) {
                continue;
            }
            (int Start, int Length) range = ((int)stackPointer.Linear - 6, 6);
            if (!excluded.Contains(range)) {
                excluded.Add(range);
            }
        }
        if (policy.AssumeAbandonedStackSlotsUnobserved) {
            foreach ((SegmentedAddress address, SegmentedAddress stackPointer) in emulated.Calls) {
                foreach ((SegmentedAddress site, int argumentBytes) in stackSpaceCallSites) {
                    if (!site.Equals(address) || argumentBytes <= 0
                        || stackPointer.Linear + argumentBytes > emulated.Ram.Length) {
                        continue;
                    }
                    (int Start, int Length) range = ((int)stackPointer.Linear, argumentBytes);
                    if (!excluded.Contains(range)) {
                        excluded.Add(range);
                    }
                }
            }
            foreach ((SegmentedAddress address, SegmentedAddress stackPointer) in emulated.Pushes) {
                foreach ((SegmentedAddress site, int bytes) in droppedStackSlotSites) {
                    if (!site.Equals(address) || bytes <= 0
                        || stackPointer.Linear < bytes) {
                        continue;
                    }
                    (int Start, int Length) range = ((int)stackPointer.Linear - bytes, bytes);
                    if (!excluded.Contains(range)) {
                        excluded.Add(range);
                    }
                }
            }
        }
        byte[] generatedRam = generated.Ram;
        if (excluded.Count > 0) {
            generatedRam = (byte[])generated.Ram.Clone();
            foreach ((int start, int length) in excluded) {
                Array.Copy(emulated.Ram, start, generatedRam, start, length);
            }
        }
        string excludedText = excluded.Count == 0
            ? string.Empty
            : $" | excluded bytes: {DescribeRanges([.. excluded.Select(range => FormatRange(range.Start, range.Start + range.Length - 1))])}";
        string ram = DescribeRam(emulated.Ram, generatedRam);
        string extended = DescribeExtended(emulated, generated);
        string line = ram == "none" && extended == "none"
            ? $"{goldenKey} | {policy.Name}{excludedText} | identical"
            : $"{goldenKey} | {policy.Name}{excludedText} | ram: {ram} | extended: {extended}";
        AppendLine(line);
        line.Should().EndWith("| identical", "the generated run must end in the interpreted run's " +
            "final state: {0}", line);
    }

    /// <summary>Appends a line for a scenario whose final-state gate is skipped: the generated
    /// execution ran with its oracle and engine assertions; only the interpreted-vs-generated
    /// final-state comparison is skipped.</summary>
    /// <param name="goldenKey">Key of the fixture.</param>
    /// <param name="reason">Why the final-state comparison is not performed.</param>
    public static void AppendGateSkipped(string goldenKey, string reason) {
        reason.Should().NotBeNullOrEmpty("a scenario that skips the final-state gate needs a reason");
        AppendLine($"{goldenKey} | final-state gate skipped | {reason}");
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
