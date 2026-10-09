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

    private static readonly Dictionary<string, (int GotoStatements, int CheckExternalEvents, int Lines)> UpperBounds = new() {
        ["jump1.spec"] = (48, 21, 402),
        ["partition_cross_function_loop.spec"] = (4, 3, 93),
        ["rep.spec"] = (6, 12, 706),
        ["div.spec"] = (0, 13, 744),
        ["selfmodifyje.spec"] = (3, 2, 63)
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
        if (!UpperBounds.TryGetValue(metrics.Fixture, out (int GotoStatements, int CheckExternalEvents, int Lines) bounds)) {
            return;
        }
        metrics.GotoStatements.Should().BeLessThanOrEqualTo(bounds.GotoStatements,
            "the GotoStatements count of {0} exceeded its recorded upper bound; raise it in the UpperBounds table on purpose, in the same PR as the golden diff",
            metrics.Fixture);
        metrics.CheckExternalEvents.Should().BeLessThanOrEqualTo(bounds.CheckExternalEvents,
            "the CheckExternalEvents count of {0} exceeded its recorded upper bound; raise it in the UpperBounds table on purpose, in the same PR as the golden diff",
            metrics.Fixture);
        metrics.Lines.Should().BeLessThanOrEqualTo(bounds.Lines,
            "the Lines count of {0} exceeded its recorded upper bound; raise it in the UpperBounds table on purpose, in the same PR as the golden diff",
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
