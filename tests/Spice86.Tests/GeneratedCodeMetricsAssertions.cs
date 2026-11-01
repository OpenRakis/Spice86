namespace Spice86.Tests;

using FluentAssertions;

using System.Text.RegularExpressions;

/// <summary>
/// Turns the <see cref="GeneratedCodeMetrics"/> of a compiled fixture into assertions that run on every
/// compile of generated code. The bounds are recorded in <c>UpperBounds</c>; raising one is a deliberate
/// edit of that table, done in the same change as the golden diff that made the number grow.
/// </summary>
internal static class GeneratedCodeMetricsAssertions {
    private static readonly Regex InterruptEnablingAsmCommentRegex = new(@"^\s*// [0-9A-F]{4}:[0-9A-F]{4} (sti|popf|popfd)$");
    private static readonly Regex AsmCommentRegex = new(@"^\s*// [0-9A-F]{4}:[0-9A-F]{4} ");
    private static readonly Regex LabelLineRegex = new(@"^\s*L_\w+:$");

    private static readonly Dictionary<string, (int GotoStatements, int CheckExternalEvents,
        int AluCalls, int StatusFlagReads, int Casts, int RawMemoryAccesses)> UpperBounds = new() {
        ["jump1.spec"] = (22, 21, 0, 48, 1, 0),
        ["partition_cross_function_loop.spec"] = (2, 3, 2, 0, 0, 0),
        ["rep.spec"] = (0, 12, 3, 3, 59, 61),
        ["div.spec"] = (0, 13, 43, 0, 75, 34),
        ["selfmodifyje.spec"] = (0, 2, 1, 0, 1, 1),
        ["test386.failport.spec"] = (9, 18, 239, 142, 58, 84),
        ["flagconditions"] = (0, 6, 20, 15, 138, 108),
        ["data_globals.spec"] = (0, 3, 0, 0, 0, 1),
        ["data_tables.spec"] = (0, 3, 1, 0, 2, 3),
        ["data_strings.ivt.spec"] = (0, 15, 6, 0, 4, 0),
        ["data_segments.spec"] = (0, 1, 1, 0, 3, 0),
        ["data_jump_tables.spec"] = (6, 7, 4, 0, 58, 64)
    };

    private static readonly Dictionary<string, int> DataCommentsLowerBounds = new() {
        ["data_strings.ivt.spec"] = 1
    };

    private static readonly Dictionary<string, int> StringServiceCallsLowerBounds = new() {
        ["data_strings.ivt.spec"] = 2
    };

    /// <summary>
    /// Asserts the invariants that every generated fixture must satisfy: no warnings, no unchecked
    /// expression, every emitted label targeted by at least one goto, an event check after every
    /// interrupt-enabling instruction, no identical CheckExternalEvents call repeated with only label
    /// lines in between, and the recorded upper bounds for the fixtures that have some.
    /// </summary>
    /// <param name="metrics">The metrics computed for the fixture.</param>
    /// <param name="source">The generated source text the metrics were computed from.</param>
    public static void AssertInvariants(GeneratedCodeMetrics metrics, string source) {
        metrics.Warnings.Should().Be(0, "generated code of {0} must compile without warnings", metrics.Fixture);
        metrics.UncheckedExpressions.Should().Be(0, "generated code of {0} must not use unchecked(...)", metrics.Fixture);
        metrics.Labels.Should().Be(metrics.DistinctGotoTargets,
            "every label emitted for {0} must be the target of at least one goto", metrics.Fixture);
        AssertEventCheckFollowsInterruptEnablingInstructions(metrics, source);
        AssertNoConsecutiveDuplicateEventChecks(metrics, source);
        AssertUpperBounds(metrics);
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

    private static void AssertUpperBounds(GeneratedCodeMetrics metrics) {
        if (DataCommentsLowerBounds.TryGetValue(metrics.Fixture, out int dataCommentsBound)) {
            metrics.DataComments.Should().BeGreaterThanOrEqualTo(dataCommentsBound,
                "the DataComments count of {0} fell below its recorded lower bound; the data comments are the readable form of the DOS calls",
                metrics.Fixture);
        }
        if (StringServiceCallsLowerBounds.TryGetValue(metrics.Fixture, out int stringCallsBound)) {
            metrics.StringServiceCalls.Should().BeGreaterThanOrEqualTo(stringCallsBound,
                "the StringServiceCalls count of {0} fell below its recorded lower bound; the literal calls are the readable form of the DOS calls",
                metrics.Fixture);
        }
        if (!UpperBounds.TryGetValue(metrics.Fixture, out (int GotoStatements, int CheckExternalEvents,
                int AluCalls, int StatusFlagReads, int Casts, int RawMemoryAccesses) bounds)) {
            return;
        }
        metrics.GotoStatements.Should().BeLessThanOrEqualTo(bounds.GotoStatements,
            "the GotoStatements count of {0} exceeded its recorded upper bound; raise it in the UpperBounds table on purpose, in the same PR as the golden diff",
            metrics.Fixture);
        metrics.CheckExternalEvents.Should().BeLessThanOrEqualTo(bounds.CheckExternalEvents,
            "the CheckExternalEvents count of {0} exceeded its recorded upper bound; raise it in the UpperBounds table on purpose, in the same PR as the golden diff",
            metrics.Fixture);
        metrics.AluCalls.Should().BeLessThanOrEqualTo(bounds.AluCalls,
            "the AluCalls count of {0} exceeded its recorded upper bound; raise it in the UpperBounds table on purpose, in the same PR as the golden diff",
            metrics.Fixture);
        metrics.StatusFlagReads.Should().BeLessThanOrEqualTo(bounds.StatusFlagReads,
            "the StatusFlagReads count of {0} exceeded its recorded upper bound; raise it in the UpperBounds table on purpose, in the same PR as the golden diff",
            metrics.Fixture);
        metrics.Casts.Should().BeLessThanOrEqualTo(bounds.Casts,
            "the Casts count of {0} exceeded its recorded upper bound; raise it in the UpperBounds table on purpose, in the same PR as the golden diff",
            metrics.Fixture);
        int rawMemoryAccesses = metrics.MemoryAccesses - metrics.GlobalAccesses - metrics.TableAccesses;
        rawMemoryAccesses.Should().BeLessThanOrEqualTo(bounds.RawMemoryAccesses,
            "the raw memory access count of {0} exceeded its recorded upper bound; raise it in the UpperBounds table on purpose, in the same PR as the golden diff",
            metrics.Fixture);
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
