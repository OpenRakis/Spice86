namespace Spice86.Tests;

using FluentAssertions;

using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.ReverseEngineer;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Turns the <see cref="GeneratedCodeMetrics"/> of a compiled fixture into assertions that run on every
/// compile of generated code. Checks correctness invariants and the reviewed per-fixture readability
/// baseline.
/// </summary>
internal static class GeneratedCodeMetricsAssertions {
    /// <summary>Minimum and maximum readability bounds of one part of one fixture.</summary>
    internal sealed record PartBounds(IReadOnlyDictionary<string, int> Minimum,
        IReadOnlyDictionary<string, int> Maximum);

    /// <summary>Bounds of one fixture, split into the program part and the support part.</summary>
    internal sealed record ReadabilityBaseline(PartBounds Program, PartBounds Support);

    internal static string BaselineDirectory =>
        Path.Join(MachineGoldens.SourceResourcesDirectory(), "GeneratedCodeReadability");

    private static readonly Lazy<IReadOnlyDictionary<string, ReadabilityBaseline>> Baseline = new(ReadBaseline);

    internal static readonly IReadOnlyList<string> MandatoryMaximumMetrics = [
        nameof(GeneratedCodeMetrics.InterruptCalls), nameof(GeneratedCodeMetrics.InterruptCallReturnedElsewhere),
        nameof(GeneratedCodeMetrics.PortWrapperCalls), nameof(GeneratedCodeMetrics.RawIoCalls),
        nameof(GeneratedCodeMetrics.RegisterReads), nameof(GeneratedCodeMetrics.RegisterWrites),
        nameof(GeneratedCodeMetrics.PlainRegisterAssignments), nameof(GeneratedCodeMetrics.LocalVariables),
        nameof(GeneratedCodeMetrics.LocalReads), nameof(GeneratedCodeMetrics.LocalWrites),
        nameof(GeneratedCodeMetrics.WriteBacks), nameof(GeneratedCodeMetrics.BookkeepingCalls),
        nameof(GeneratedCodeMetrics.Statements), nameof(GeneratedCodeMetrics.Casts)
    ];

    internal static readonly IReadOnlyList<string> MandatoryMinimumMetrics =
        typeof(CSharpOverrideHelper).GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(property => typeof(IInterruptHandler).IsAssignableFrom(property.PropertyType))
            .Select(property => $"serviceCalls.{property.Name}")
            .Concat(typeof(CSharpOverrideHelper).GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(property => DeviceOperationRegistry.ReceiverType(property.Name) is not null)
                .Select(property => $"deviceCalls.{property.Name}"))
            .Append(nameof(GeneratedCodeMetrics.StringServiceCalls)).Order(StringComparer.Ordinal).ToArray();

    private static readonly HashSet<string> MinimumMetrics = new(
        MandatoryMinimumMetrics.Concat([
            nameof(GeneratedCodeMetrics.DataComments), nameof(GeneratedCodeMetrics.LocationAccesses),
            nameof(GeneratedCodeMetrics.GlobalAccesses), nameof(GeneratedCodeMetrics.TableAccesses),
            nameof(GeneratedCodeMetrics.FrameAccesses)
        ]), StringComparer.Ordinal);

    internal static IReadOnlyDictionary<string, ReadabilityBaseline> LoadBaseline() => Baseline.Value;

    private static IReadOnlyDictionary<string, ReadabilityBaseline> ReadBaseline() {
        return ParseBaseline(File.ReadAllText(Path.Join(BaselineDirectory, "bounds.tsv")));
    }

    internal static ReadabilityBaseline GetBaseline(string fixture,
        IReadOnlyDictionary<string, ReadabilityBaseline> baselines) {
        if (!baselines.TryGetValue(fixture, out ReadabilityBaseline? baseline)) {
            throw new InvalidDataException($"Missing readability baseline for fixture '{fixture}'.");
        }
        return baseline;
    }

    private static string HeaderLine => string.Join('\t',
        new[] { "key", "part" }.Concat(MandatoryMaximumMetrics).Append("other"));

    internal static IReadOnlyDictionary<string, ReadabilityBaseline> ParseBaseline(string table) {
        string[] lines = table.Replace("\r\n", "\n").Split('\n');
        if (lines.Length != 0 && lines[^1].Length == 0) {
            lines = lines[..^1];
        }
        if (lines.Length == 0 || lines[0] != HeaderLine) {
            throw new InvalidDataException("Readability baseline table header does not match the expected columns.");
        }
        int fieldCount = 2 + MandatoryMaximumMetrics.Count + 1;
        Dictionary<string, Dictionary<string, PartBounds>> rows = new(StringComparer.Ordinal);
        for (int lineIndex = 1; lineIndex < lines.Length; lineIndex++) {
            string[] fields = lines[lineIndex].Split('\t');
            if (fields.Length != fieldCount) {
                throw new InvalidDataException($"Readability baseline row {lineIndex + 1} has the wrong field count.");
            }
            string key = fields[0];
            string part = fields[1];
            if (key.Length == 0) {
                throw new InvalidDataException("A readability baseline key must not be empty.");
            }
            if (part is not ("program" or "support")) {
                throw new InvalidDataException($"{key}: readability baseline part must be program or support.");
            }
            Dictionary<string, int> maximum = new(StringComparer.Ordinal);
            for (int column = 0; column < MandatoryMaximumMetrics.Count; column++) {
                maximum.Add(MandatoryMaximumMetrics[column], ParseCount(fields[2 + column], key));
            }
            Dictionary<string, int> minimum = new(StringComparer.Ordinal);
            ReadOther(fields[^1], key, minimum, maximum);
            if (!rows.TryGetValue(key, out Dictionary<string, PartBounds>? parts)) {
                parts = new Dictionary<string, PartBounds>(StringComparer.Ordinal);
                rows.Add(key, parts);
            }
            if (!parts.TryAdd(part, new PartBounds(minimum, maximum))) {
                throw new InvalidDataException($"{key}: duplicate readability baseline row for {part}.");
            }
        }
        SortedDictionary<string, ReadabilityBaseline> baselines = new(StringComparer.Ordinal);
        foreach ((string key, Dictionary<string, PartBounds> parts) in rows) {
            if (!parts.TryGetValue("program", out PartBounds? program)
                || !parts.TryGetValue("support", out PartBounds? support) || parts.Count != 2) {
                throw new InvalidDataException($"{key}: readability baseline requires one program row and one support row.");
            }
            baselines.Add(key, new ReadabilityBaseline(program, support));
        }
        return baselines;
    }

    private static int ParseCount(string text, string key) {
        if (text.Length == 0 || !text.All(char.IsDigit) || !int.TryParse(text, out int value)) {
            throw new InvalidDataException($"{key}: readability bound '{text}' is not a non-negative integer.");
        }
        return value;
    }

    private static void ReadOther(string text, string key,
        Dictionary<string, int> minimum, Dictionary<string, int> maximum) {
        if (text == "-") {
            return;
        }
        if (text.Length == 0) {
            throw new InvalidDataException($"{key}: malformed readability bounds in other.");
        }
        foreach (string item in text.Split(' ')) {
            int separator = item.IndexOfAny(['<', '>']);
            if (separator <= 0 || separator + 1 >= item.Length || item[separator + 1] != '=') {
                throw new InvalidDataException($"{key}: malformed readability bound '{item}'.");
            }
            string name = item[..separator];
            bool minimumDirection = item[separator] == '>';
            int value = ParseCount(item[(separator + 2)..], key);
            ValidateMetric(name, minimumDirection);
            if (!minimumDirection && MandatoryMaximumMetrics.Contains(name, StringComparer.Ordinal)) {
                throw new InvalidDataException($"{key}: mandatory maximum '{name}' must use its table column.");
            }
            Dictionary<string, int> target = minimumDirection ? minimum : maximum;
            if (!target.TryAdd(name, value)) {
                throw new InvalidDataException($"{key}: duplicate readability bound '{name}'.");
            }
        }
    }

    private static void ValidateMetric(string key, bool minimum) {
        bool isMinimum = MinimumMetrics.Contains(key);
        bool isMaximum = key is "RawMemoryAccesses" or "AluCalls+StatusFlagReads" or "AluCalls+Casts"
            || typeof(GeneratedCodeMetrics).GetProperty(key) is { PropertyType: { } type } && type == typeof(int);
        if (key is nameof(GeneratedCodeMetrics.RegisterLocals) or nameof(GeneratedCodeMetrics.RegisterLocalsMulti)
            or nameof(GeneratedCodeMetrics.SignedLocals) or nameof(GeneratedCodeMetrics.TypedServiceCalls)
            or nameof(GeneratedCodeMetrics.DeviceCalls)) {
            isMaximum = false;
        }
        if (minimum ? !isMinimum : !isMaximum || isMinimum) {
            throw new InvalidDataException($"Unknown readability metric or invalid bound direction: '{key}'.");
        }
    }

    private static readonly object TighteningGate = new();
    private static readonly Dictionary<(string Fixture, string Part, bool Minimum, string Key), int>
        LoosestMeasured = new();
    private static readonly Dictionary<string, ReadabilityBaseline> SeededBaselines = new(StringComparer.Ordinal);

    /// <summary>Tightens the bounds table toward the values this run measured: a maximum goes down to
    /// the largest measured value, a minimum up to the smallest, never past the baseline.</summary>
    internal static void WriteTightenedBounds(
        GeneratedCodeMetricsCollector.GeneratedCodePartMetrics parts) {
        string fixture = parts.Program.Fixture;
        lock (TighteningGate) {
            IReadOnlyDictionary<string, ReadabilityBaseline> baselines = LoadBaseline();
            if (!baselines.TryGetValue(fixture, out ReadabilityBaseline? baseline)
                && !SeededBaselines.TryGetValue(fixture, out baseline)) {
                baseline = new ReadabilityBaseline(UnboundedPart(), UnboundedPart());
                SeededBaselines.Add(fixture, baseline);
            }
            Accumulate(parts.Program, "program", fixture, baseline.Program);
            Accumulate(parts.Support, "support", fixture, baseline.Support);
            Dictionary<string, ReadabilityBaseline> rows = new(StringComparer.Ordinal);
            foreach ((string key, ReadabilityBaseline row) in baselines) {
                rows[key] = TightenedBaseline(key, row);
            }
            foreach ((string key, ReadabilityBaseline row) in SeededBaselines) {
                rows[key] = TightenedBaseline(key, row);
            }
            File.WriteAllText(Path.Join(BaselineDirectory, "bounds.tsv"), FormatBaseline(rows));
        }
    }

    private static PartBounds UnboundedPart() {
        Dictionary<string, int> maximum = MandatoryMaximumMetrics
            .ToDictionary(key => key, _ => int.MaxValue, StringComparer.Ordinal);
        return new PartBounds(new Dictionary<string, int>(StringComparer.Ordinal), maximum);
    }

    private static void Accumulate(GeneratedCodeMetrics metrics, string part, string fixture, PartBounds bounds) {
        IEnumerable<string> minimumKeys = MandatoryMinimumMetrics.Concat(bounds.Minimum.Keys).Distinct(StringComparer.Ordinal);
        Record(metrics, part, fixture, minimumKeys, true);
        Record(metrics, part, fixture, bounds.Maximum.Keys, false);
    }

    private static void Record(GeneratedCodeMetrics metrics, string part, string fixture,
        IEnumerable<string> keys, bool minimum) {
        foreach (string key in keys) {
            (string, string, bool, string) boundKey = (fixture, part, minimum, key);
            int measured = MetricValue(metrics, key);
            if (!LoosestMeasured.TryGetValue(boundKey, out int seen)) {
                LoosestMeasured[boundKey] = measured;
                continue;
            }
            LoosestMeasured[boundKey] = minimum ? Math.Min(seen, measured) : Math.Max(seen, measured);
        }
    }

    private static ReadabilityBaseline TightenedBaseline(string fixture, ReadabilityBaseline baseline) {
        return new ReadabilityBaseline(TightenedPart(fixture, "program", baseline.Program),
            TightenedPart(fixture, "support", baseline.Support));
    }

    private static PartBounds TightenedPart(string fixture, string part, PartBounds bounds) {
        Dictionary<string, int> minimum = new(StringComparer.Ordinal);
        foreach (string key in MandatoryMinimumMetrics.Concat(bounds.Minimum.Keys).Distinct(StringComparer.Ordinal)) {
            int baseline = bounds.Minimum.GetValueOrDefault(key, 0);
            int measured = LoosestMeasured.GetValueOrDefault((fixture, part, true, key), int.MinValue);
            minimum[key] = Math.Max(baseline, measured);
        }
        Dictionary<string, int> maximum = new(StringComparer.Ordinal);
        foreach ((string key, int baseline) in bounds.Maximum) {
            int measured = LoosestMeasured.GetValueOrDefault((fixture, part, false, key), int.MaxValue);
            maximum[key] = Math.Min(baseline, measured);
        }
        return new PartBounds(minimum, maximum);
    }

    internal static string FormatBaseline(IReadOnlyDictionary<string, ReadabilityBaseline> baselines) {
        StringBuilder builder = new();
        builder.Append(HeaderLine).Append('\n');
        foreach ((string key, ReadabilityBaseline baseline) in baselines.OrderBy(pair => pair.Key, StringComparer.Ordinal)) {
            AppendRow(builder, key, "program", baseline.Program);
            AppendRow(builder, key, "support", baseline.Support);
        }
        return builder.ToString();
    }

    private static void AppendRow(StringBuilder builder, string key, string part, PartBounds bounds) {
        builder.Append(key).Append('\t').Append(part);
        foreach (string metric in MandatoryMaximumMetrics) {
            if (!bounds.Maximum.TryGetValue(metric, out int value)) {
                throw new InvalidDataException($"{key}: missing mandatory readability maximum '{metric}'.");
            }
            builder.Append('\t').Append(value);
        }
        SortedDictionary<string, string> other = new(StringComparer.Ordinal);
        foreach ((string metric, int value) in bounds.Minimum) {
            if (value > 0) {
                other[metric] = $"{metric}>={value}";
            }
        }
        foreach ((string metric, int value) in bounds.Maximum) {
            if (!MandatoryMaximumMetrics.Contains(metric, StringComparer.Ordinal)) {
                other[metric] = $"{metric}<={value}";
            }
        }
        builder.Append('\t').Append(other.Count == 0 ? "-" : string.Join(' ', other.Values)).Append('\n');
    }

    internal static int MetricValue(GeneratedCodeMetrics metrics, string key) {
        if (key.StartsWith("serviceCalls.", StringComparison.Ordinal)) {
            return metrics.ServiceCallsByReceiver.GetValueOrDefault(key["serviceCalls.".Length..]);
        }
        if (key.StartsWith("deviceCalls.", StringComparison.Ordinal)) {
            return metrics.DeviceCallsByReceiver.GetValueOrDefault(key["deviceCalls.".Length..]);
        }
        return key switch {
            "RawMemoryAccesses" => metrics.MemoryAccesses - metrics.GlobalAccesses - metrics.TableAccesses,
            "AluCalls+StatusFlagReads" => metrics.AluCalls + metrics.StatusFlagReads,
            "AluCalls+Casts" => metrics.AluCalls + metrics.Casts,
            _ => typeof(GeneratedCodeMetrics).GetProperty(key)?.GetValue(metrics) as int?
                ?? throw new InvalidDataException($"Unknown readability metric '{key}'.")
        };
    }

    internal static IReadOnlyList<string> FindReadabilityRegressions(GeneratedCodeMetrics actual,
        string part, IReadOnlyDictionary<string, int> minimum, IReadOnlyDictionary<string, int> maximum) {
        List<string> failures = new();
        AppendRegressions(actual, part, minimum, true, failures);
        AppendRegressions(actual, part, maximum, false, failures);
        return failures;
    }

    private static void AppendRegressions(GeneratedCodeMetrics actual, string part,
        IReadOnlyDictionary<string, int> bounds, bool minimum, List<string> failures) {
        foreach ((string key, int limit) in bounds.OrderBy(pair => pair.Key, StringComparer.Ordinal)) {
            ValidateMetric(key, minimum);
            if (limit < 0) {
                throw new InvalidDataException($"Negative readability bound for '{key}'.");
            }
            int count = MetricValue(actual, key);
            long delta = (long)count - limit;
            if (minimum ? delta < 0 : delta > 0) {
                string direction = minimum ? "minimum" : "maximum";
                failures.Add($"{actual.Fixture} {part}: {key} {direction}={limit}, actual={count}, delta={delta:+0;-0;0}.");
            }
        }
    }

    private static readonly Regex InterruptEnablingAsmCommentRegex = new(@"^\s*// [0-9A-F]{4}:[0-9A-F]{4} (sti|popf|popfd)$");
    private static readonly Regex AsmCommentRegex = new(@"^\s*// [0-9A-F]{4}:[0-9A-F]{4} ");
    private static readonly Regex LabelLineRegex = new(@"^\s*L_\w+:$");

    /// <summary>
    /// Asserts the invariants that every generated fixture must satisfy: no warnings, no unchecked
    /// expression, every emitted label targeted by at least one goto, an event check after every
    /// interrupt-enabling instruction, no identical CheckExternalEvents call repeated with only label
    /// lines in between, and the reviewed per-fixture readability baseline.
    /// </summary>
    /// <param name="parts">The program and support metrics computed for the fixture.</param>
    /// <param name="source">The generated source text the metrics were computed from.</param>
    /// <param name="methods">Method counts and source locations for regression diagnostics.</param>
    public static void AssertInvariants(GeneratedCodeMetricsCollector.GeneratedCodePartMetrics parts, string source,
        IReadOnlyDictionary<string, GeneratedCodeMetrics> methods) {
        foreach (GeneratedCodeMetrics metrics in parts.Parts) {
            metrics.Warnings.Should().Be(0, "generated code of {0} must compile without warnings", metrics.Fixture);
            metrics.UncheckedExpressions.Should().Be(0, "generated code of {0} must not use unchecked(...)", metrics.Fixture);
            metrics.Labels.Should().Be(metrics.DistinctGotoTargets,
                "every label emitted for {0} must be the target of at least one goto", metrics.Fixture);
            AssertEventCheckFollowsInterruptEnablingInstructions(metrics, source);
            AssertNoConsecutiveDuplicateEventChecks(metrics, source);
        }
        ReadabilityBaseline baseline = GetBaseline(parts.Program.Fixture, LoadBaseline());
        List<string> failures = new(FindReadabilityRegressions(parts.Program, "program",
            baseline.Program.Minimum, baseline.Program.Maximum));
        failures.AddRange(FindReadabilityRegressions(parts.Support, "support",
            baseline.Support.Minimum, baseline.Support.Maximum));
        if (failures.Count != 0) {
            string details = string.Join(Environment.NewLine, methods.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{pair.Key} {pair.Value.SourceLocation}: {pair.Value.ToLine()}"));
            failures.Should().BeEmpty("generated code of {0} must preserve its readability bounds. Method details:{1}{2}",
                parts.Program.Fixture, Environment.NewLine, details);
        }
    }

    private static void AssertNoConsecutiveDuplicateEventChecks(GeneratedCodeMetrics metrics, string source) {
        List<string> duplicates = ConsecutiveDuplicateExternalEventChecks(SplitLines(source));
        duplicates.Should().BeEmpty("generated code of {0} must not repeat an identical CheckExternalEvents call", metrics.Fixture);
    }

    private static List<string> ConsecutiveDuplicateExternalEventChecks(string[] lines) {
        List<string> duplicates = new();
        string? lastCheckLine = null;
        for (int i = 0; i < lines.Length; i++) {
            string trimmed = lines[i].Trim();
            if (LabelLineRegex.IsMatch(trimmed)) {
                continue;
            }
            if (trimmed.Length == 0) {
                continue;
            }
            if (trimmed.Contains("CheckExternalEvents(")) {
                if (lastCheckLine != null && trimmed == lastCheckLine) {
                    duplicates.Add(trimmed);
                }
                lastCheckLine = trimmed;
            } else {
                lastCheckLine = null;
            }
        }
        return duplicates;
    }

    private static void AssertEventCheckFollowsInterruptEnablingInstructions(GeneratedCodeMetrics metrics, string source) {
        string[] lines = SplitLines(source);
        for (int i = 0; i < lines.Length; i++) {
            if (!InterruptEnablingAsmCommentRegex.IsMatch(lines[i])) {
                continue;
            }
            bool foundCheck = HasEventCheckBeforeNextInstruction(lines, i);
            foundCheck.Should().BeTrue(
                "generated code of {0} must call CheckExternalEvents after the interrupt-enabling instruction at line {1}: {2}",
                metrics.Fixture, i + 1, lines[i].Trim());
        }
    }

    private static bool HasEventCheckBeforeNextInstruction(string[] lines, int instructionLineIndex) {
        for (int j = instructionLineIndex + 1; j < lines.Length; j++) {
            if (AsmCommentRegex.IsMatch(lines[j])) {
                return false;
            }
            if (lines[j].Contains("CheckExternalEvents(")) {
                return true;
            }
        }
        return false;
    }

    private static string[] SplitLines(string source) {
        return source.Replace("\r\n", "\n").Split('\n');
    }
}
