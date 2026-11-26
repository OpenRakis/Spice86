namespace Spice86.Tests;

using FluentAssertions;

using System.Reflection;

using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
using Spice86.Core.Emulator.VM;
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
    public static string Check(string goldenKey, AsmFinalState emulated, AsmFinalState generated) =>
        Check(goldenKey, GenerationPolicy.Strict, emulated, generated);

    /// <summary>Appends the comparison of <paramref name="emulated"/> and <paramref name="generated"/>
    /// to the report and fails when their compared final states differ. Final registers are not
    /// compared: a program-ending <c>hlt</c> observes memory only.</summary>
    /// <param name="goldenKey">Key of the fixture that produced both states.</param>
    /// <param name="policy">The generation policy the comparison ran under.</param>
    /// <param name="emulated">Final state of the interpreted run.</param>
    /// <param name="generated">Final state of the generated run.</param>
    public static string Check(string goldenKey, GenerationPolicy policy, AsmFinalState emulated,
        AsmFinalState generated) =>
        Check(goldenKey, policy, emulated, generated, new HashSet<SegmentedAddress>(), [], [],
            FinalStateComparison.Strict);

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
    public static string Check(string goldenKey, GenerationPolicy policy, AsmFinalState emulated,
        AsmFinalState generated, IReadOnlySet<SegmentedAddress> framelessServiceCallSites) =>
        Check(goldenKey, policy, emulated, generated, framelessServiceCallSites, [], [],
            FinalStateComparison.Strict);

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
    /// <param name="comparison">How the comparison applies: a timing reason that leaves RAM and the
    /// extended regions out, and the final-state parts left out with their reason.</param>
    /// <returns>The report line describing the comparison.</returns>
    public static string Check(string goldenKey, GenerationPolicy policy, AsmFinalState emulated,
        AsmFinalState generated, IReadOnlySet<SegmentedAddress> framelessServiceCallSites,
        IReadOnlyList<(SegmentedAddress Address, int ArgumentBytes)> stackSpaceCallSites,
        IReadOnlyList<(SegmentedAddress Address, int Bytes)> droppedStackSlotSites,
        FinalStateComparison comparison) {
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
        List<string> uncomparedRamReasons = [];
        foreach ((uint address, int length, string reason) in comparison.UncomparedRamRanges) {
            if (address + length > emulated.Ram.Length) {
                continue;
            }
            (int Start, int Length) range = ((int)address, length);
            if (!excluded.Contains(range)) {
                excluded.Add(range);
            }
            if (!uncomparedRamReasons.Contains(reason)) {
                uncomparedRamReasons.Add(reason);
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
        if (uncomparedRamReasons.Count > 0) {
            excludedText += $" ({string.Join(", ", uncomparedRamReasons)})";
        }
        List<string> differences = [];
        if (comparison.TimingDependentReason is null) {
            string ram = DescribeRam(emulated.Ram, generatedRam);
            string extended = DescribeExtended(emulated, generated);
            if (ram != "none" || extended != "none") {
                differences.Add($"ram: {ram}");
                differences.Add($"extended: {extended}");
            }
        }
        if (!comparison.UncomparedParts.HasFlag(FinalStateParts.Console)
            && emulated.ConsoleOutput != generated.ConsoleOutput) {
            differences.Add($"console: {DescribeConsole(emulated.ConsoleOutput, generated.ConsoleOutput)}");
        }
        if (!comparison.UncomparedParts.HasFlag(FinalStateParts.DriveFiles)) {
            string files = DescribeFiles(emulated, generated);
            if (files != "none") {
                differences.Add($"files: {files}");
            }
        }
        if (!comparison.UncomparedParts.HasFlag(FinalStateParts.DeviceRegisters)) {
            List<string> deviceDifferences = [];
            string registers = DescribeDeviceRegisters(emulated, generated);
            if (registers != "none") {
                deviceDifferences.Add(registers);
            }
            string states = DescribeDeviceStates(emulated, generated);
            if (states != "none") {
                deviceDifferences.Add(states);
            }
            if (deviceDifferences.Count > 0) {
                differences.Add($"devices: {string.Join("; ", deviceDifferences)}");
            }
        }
        string uncompared = comparison.UncomparedParts == FinalStateParts.None
            ? string.Empty
            : $" | uncompared: {DescribeUncomparedParts(comparison.UncomparedParts)} ({comparison.UncomparedPartsReason})";
        string policyField = comparison.TimingDependentReason is null
            ? $"{policy.Name}{excludedText}"
            : "timing-dependent";
        string result;
        if (differences.Count == 0) {
            result = comparison.TimingDependentReason ?? "identical";
        } else if (comparison.TimingDependentReason is null) {
            result = string.Join(" | ", differences);
        } else {
            result = $"{comparison.TimingDependentReason} | {string.Join(" | ", differences)}";
        }
        string line = $"{goldenKey} | {policyField}{uncompared} | {result}";
        AppendLine(line);
        differences.Should().BeEmpty("the generated run must end in the interpreted run's " +
            "final state: {0}", line);
        return line;
    }

    private static string DescribeUncomparedParts(FinalStateParts parts) {
        List<string> names = [];
        if (parts.HasFlag(FinalStateParts.Console)) {
            names.Add("console");
        }
        if (parts.HasFlag(FinalStateParts.DriveFiles)) {
            names.Add("files");
        }
        if (parts.HasFlag(FinalStateParts.DeviceRegisters)) {
            names.Add("devices");
        }
        return string.Join(", ", names);
    }

    private static string DescribeConsole(string emulated, string generated) {
        int index = 0;
        while (index < emulated.Length && index < generated.Length
               && emulated[index] == generated[index]) {
            index++;
        }
        string emulatedChar = index < emulated.Length ? $"'{emulated[index]}'" : "<end>";
        string generatedChar = index < generated.Length ? $"'{generated[index]}'" : "<end>";
        return $"index {index} interpreted={emulatedChar} generated={generatedChar} " +
            $"(length {emulated.Length}->{generated.Length})";
    }

    private static string DescribeFiles(AsmFinalState emulated, AsmFinalState generated) {
        List<string> differences = [];
        Dictionary<string, byte[]> generatedFiles =
            generated.DriveFiles.ToDictionary(file => file.Path, file => file.Content);
        HashSet<string> seen = [];
        foreach ((string path, byte[] content) in emulated.DriveFiles) {
            seen.Add(path);
            if (!generatedFiles.TryGetValue(path, out byte[]? other)) {
                differences.Add($"{path} missing in generated");
                continue;
            }
            int offset = 0;
            while (offset < content.Length && offset < other.Length
                   && content[offset] == other[offset]) {
                offset++;
            }
            if (offset < content.Length || offset < other.Length) {
                differences.Add($"{path} first differing byte 0x{offset:X}");
            }
        }
        foreach ((string path, _) in generated.DriveFiles) {
            if (!seen.Contains(path)) {
                differences.Add($"{path} extra in generated");
            }
        }
        return differences.Count == 0 ? "none" : $"{differences.Count} files differ, first: {differences[0]}";
    }

    private static string DescribeDeviceRegisters(AsmFinalState emulated, AsmFinalState generated) {
        List<string> differences = [];
        Dictionary<(string Device, string Register), uint> generatedRegisters =
            generated.DeviceRegisters.ToDictionary(r => (r.Device, r.Register), r => r.Value);
        HashSet<(string Device, string Register)> seen = [];
        foreach ((string device, string register, uint value) in emulated.DeviceRegisters) {
            seen.Add((device, register));
            if (!generatedRegisters.TryGetValue((device, register), out uint other)) {
                differences.Add($"{device} {register} missing in generated");
            } else if (other != value) {
                differences.Add($"{device} {register} interpreted=0x{value:X} generated=0x{other:X}");
            }
        }
        foreach ((string device, string register, _) in generated.DeviceRegisters) {
            if (!seen.Contains((device, register))) {
                differences.Add($"{device} {register} extra in generated");
            }
        }
        return differences.Count == 0 ? "none" : $"{differences.Count} registers differ, first: {differences[0]}";
    }

    /// <summary>The device states compared by their record equality. The other members of
    /// <see cref="MachineDeviceState"/> hold byte arrays or immutable collections, whose equality is
    /// by reference: VGA registers and DAC stay in <see cref="AsmFinalState.DeviceRegisters"/>, XMS
    /// and EMS memory stays in the extended regions, and the rest is left out.</summary>
    private static readonly (string Name, Func<MachineDeviceState, object?> Get)[] ComparedDeviceStates = [
        ("pic", state => state.Pic),
        ("pit", state => state.Pit),
        ("mouse", state => state.MouseDevice),
        ("mouse-driver", state => state.MouseDriver),
        ("dma", state => state.Dma),
        ("a20", state => state.A20Gate)
    ];

    private static string DescribeDeviceStates(AsmFinalState emulated, AsmFinalState generated) {
        List<string> differences = [];
        if (emulated.DeviceState is not { } emulatedState || generated.DeviceState is not { } generatedState) {
            if (emulated.DeviceState is not null || generated.DeviceState is not null) {
                return "device state captured in only one run";
            }
            return "none";
        }
        foreach ((string name, Func<MachineDeviceState, object?> get) in ComparedDeviceStates) {
            object? emulatedDevice = get(emulatedState);
            object? generatedDevice = get(generatedState);
            if (emulatedDevice is null && generatedDevice is null) {
                continue;
            }
            if (emulatedDevice is null || generatedDevice is null) {
                differences.Add($"{name} present in {(emulatedDevice is null ? "generated" : "interpreted")} only");
                continue;
            }
            if (!emulatedDevice.Equals(generatedDevice)) {
                differences.Add($"{name} {FirstDifferingProperty(emulatedDevice, generatedDevice)}");
            }
        }
        return differences.Count == 0 ? "none" : $"{differences.Count} devices differ, first: {differences[0]}";
    }

    /// <summary>Names the first property of the device state record that differs, for the report
    /// message only; the comparison itself is the record's own equality.</summary>
    private static string FirstDifferingProperty(object emulated, object generated) {
        foreach (PropertyInfo property in emulated.GetType().GetProperties()) {
            object? emulatedValue = property.GetValue(emulated);
            object? generatedValue = property.GetValue(generated);
            if (!Equals(emulatedValue, generatedValue)) {
                return $"{property.Name} interpreted={emulatedValue} generated={generatedValue}";
            }
        }
        return "differ";
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
