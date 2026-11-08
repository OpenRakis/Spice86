namespace Spice86.Tests;

using FluentAssertions;

using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.ReverseEngineer;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

/// <summary>
/// Turns the <see cref="GeneratedCodeMetrics"/> of a compiled fixture into assertions that run on every
/// compile of generated code. Checks correctness invariants and the reviewed per-fixture readability
/// baseline.
/// </summary>
internal static class GeneratedCodeMetricsAssertions {
    internal sealed record ReadabilityBaseline(string SourceSha256,
        IReadOnlyDictionary<string, int> Minimum, IReadOnlyDictionary<string, int> Maximum);

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
        string metadata = File.ReadAllText(Path.Join(BaselineDirectory, "metadata.json"));
        Dictionary<string, string> fixtures = Directory.EnumerateFiles(BaselineDirectory, "*.json")
            .Where(path => Path.GetFileName(path) != "metadata.json")
            .Order(StringComparer.Ordinal)
            .ToDictionary(path => Path.GetFileNameWithoutExtension(path), File.ReadAllText, StringComparer.Ordinal);
        return ParseBaseline(metadata, fixtures);
    }

    internal static ReadabilityBaseline GetBaseline(string fixture,
        IReadOnlyDictionary<string, ReadabilityBaseline> baselines) {
        if (!baselines.TryGetValue(fixture, out ReadabilityBaseline? baseline)) {
            throw new InvalidDataException($"Missing readability baseline for fixture '{fixture}'.");
        }
        return baseline;
    }

    internal static IReadOnlyDictionary<string, ReadabilityBaseline> ParseBaseline(
        string metadataJson, IReadOnlyDictionary<string, string> fixtureSources) {
        using JsonDocument document = JsonDocument.Parse(metadataJson);
        JsonElement root = document.RootElement;
        RequireFields(root, "schemaVersion", "referenceRevision");
        if (root.GetProperty("schemaVersion").ValueKind != JsonValueKind.Number
            || !root.GetProperty("schemaVersion").TryGetInt32(out int schemaVersion) || schemaVersion != 2) {
            throw new InvalidDataException("Readability baseline schemaVersion must be 2.");
        }
        RequireHash(root.GetProperty("referenceRevision"), 40, "referenceRevision");
        SortedDictionary<string, ReadabilityBaseline> baselines = new(StringComparer.Ordinal);
        foreach ((string fixture, string source) in fixtureSources) {
            if (string.IsNullOrWhiteSpace(fixture)) {
                throw new InvalidDataException("A readability fixture key must not be empty.");
            }
            using JsonDocument fixtureDocument = JsonDocument.Parse(source);
            JsonElement entry = fixtureDocument.RootElement;
            RequireFields(entry, "sourceSha256", "minimum", "maximum");
            string hash = RequireHash(entry.GetProperty("sourceSha256"), 64, fixture);
            IReadOnlyDictionary<string, int> minimum = ReadBounds(entry.GetProperty("minimum"), true, fixture);
            IReadOnlyDictionary<string, int> maximum = ReadBounds(entry.GetProperty("maximum"), false, fixture);
            RequireMetrics(minimum, MandatoryMinimumMetrics, fixture);
            RequireMetrics(maximum, MandatoryMaximumMetrics, fixture);
            baselines.Add(fixture, new(hash, minimum, maximum));
        }
        return baselines;
    }

    private static void RequireObject(JsonElement element, string name) {
        if (element.ValueKind != JsonValueKind.Object) {
            throw new InvalidDataException($"Readability baseline '{name}' must be an object.");
        }
    }

    private static void RequireFields(JsonElement element, params string[] names) {
        RequireObject(element, string.Join("/", names));
        string[] actual = element.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();
        if (!actual.SequenceEqual(names.Order(StringComparer.Ordinal))) {
            throw new InvalidDataException($"Readability baseline requires exactly: {string.Join(", ", names)}.");
        }
    }

    private static string RequireHash(JsonElement element, int length, string name) {
        if (element.ValueKind != JsonValueKind.String || element.GetString() is not { } hash
            || hash.Length != length || !hash.All(char.IsAsciiHexDigit)) {
            throw new InvalidDataException($"Readability baseline '{name}' requires a {length}-digit hexadecimal hash.");
        }
        return hash;
    }

    private static void RequireMetrics(IReadOnlyDictionary<string, int> bounds, IReadOnlyList<string> required, string fixture) {
        string[] missing = required.Where(key => !bounds.ContainsKey(key)).ToArray();
        if (missing.Length != 0) {
            throw new InvalidDataException($"{fixture}: missing readability metrics: {string.Join(", ", missing)}.");
        }
    }

    private static IReadOnlyDictionary<string, int> ReadBounds(JsonElement element, bool minimum, string fixture) {
        RequireObject(element, fixture);
        SortedDictionary<string, int> bounds = new(StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject()) {
            ValidateMetric(property.Name, minimum);
            if (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetInt32(out int value)
                || value < 0 || !bounds.TryAdd(property.Name, value)) {
                throw new InvalidDataException($"{fixture}: invalid or duplicate readability bound '{property.Name}'.");
            }
        }
        return bounds;
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
        IReadOnlyDictionary<string, int> minimum, IReadOnlyDictionary<string, int> maximum) {
        List<string> failures = new();
        AppendRegressions(actual, minimum, true, failures);
        AppendRegressions(actual, maximum, false, failures);
        return failures;
    }

    private static void AppendRegressions(GeneratedCodeMetrics actual, IReadOnlyDictionary<string, int> bounds,
        bool minimum, List<string> failures) {
        foreach ((string key, int limit) in bounds.OrderBy(pair => pair.Key, StringComparer.Ordinal)) {
            ValidateMetric(key, minimum);
            if (limit < 0) {
                throw new InvalidDataException($"Negative readability bound for '{key}'.");
            }
            int count = MetricValue(actual, key);
            long delta = (long)count - limit;
            if (minimum ? delta < 0 : delta > 0) {
                string direction = minimum ? "minimum" : "maximum";
                failures.Add($"{actual.Fixture}: {key} {direction}={limit}, actual={count}, delta={delta:+0;-0;0}.");
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
    /// <param name="metrics">The metrics computed for the fixture.</param>
    /// <param name="source">The generated source text the metrics were computed from.</param>
    /// <param name="methods">Method counts and source locations for regression diagnostics.</param>
    public static void AssertInvariants(GeneratedCodeMetrics metrics, string source,
        IReadOnlyDictionary<string, GeneratedCodeMetrics> methods) {
        metrics.Warnings.Should().Be(0, "generated code of {0} must compile without warnings", metrics.Fixture);
        metrics.UncheckedExpressions.Should().Be(0, "generated code of {0} must not use unchecked(...)", metrics.Fixture);
        metrics.Labels.Should().Be(metrics.DistinctGotoTargets,
            "every label emitted for {0} must be the target of at least one goto", metrics.Fixture);
        AssertEventCheckFollowsInterruptEnablingInstructions(metrics, source);
        AssertNoConsecutiveDuplicateEventChecks(metrics, source);
        ReadabilityBaseline baseline = GetBaseline(metrics.Fixture, LoadBaseline());
        IReadOnlyList<string> failures = FindReadabilityRegressions(metrics, baseline.Minimum, baseline.Maximum);
        if (failures.Count != 0) {
            string details = string.Join(Environment.NewLine, methods.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{pair.Key} {pair.Value.SourceLocation}: {pair.Value.ToLine()}"));
            failures.Should().BeEmpty("generated code of {0} must preserve its readability bounds. Method details:{1}{2}",
                metrics.Fixture, Environment.NewLine, details);
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
