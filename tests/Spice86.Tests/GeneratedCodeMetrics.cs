namespace Spice86.Tests;

using System.Collections.Immutable;
using System.Linq;

/// <summary>
/// Readability metrics for a single generated-code fixture, computed from the Roslyn syntax tree and the
/// compilation diagnostics. Used by the code-generator quality harness to track how later phases change the
/// generated output. Each row is written to <c>metrics.txt</c> and checked by
/// <see cref="GeneratedCodeMetricsAssertions"/> on every compile.
/// </summary>
internal sealed record GeneratedCodeMetrics {
    public required string Fixture { get; init; }
    public required int Methods { get; init; }
    public required int Lines { get; init; }
    public required int Labels { get; init; }
    public required int GotoStatements { get; init; }
    /// <summary>Distinct goto target identifiers summed per method (not file-wide), since the same label text can appear in multiple methods.</summary>
    public required int DistinctGotoTargets { get; init; }
    public required int CheckExternalEvents { get; init; }
    public required int VerifySpeculativeEntryOrFail { get; init; }
    public required int FailAsUntested { get; init; }
    public required int UncheckedExpressions { get; init; }
    public required int Casts { get; init; }
    public required int TryStatements { get; init; }
    /// <summary>Invocations of any method on <c>Alu8</c>, <c>Alu16</c> or <c>Alu32</c>.</summary>
    public required int AluCalls { get; init; }
    /// <summary>Reads of the six status flags (C, P, A, Z, S, O); assignments to them are not counted.</summary>
    public required int StatusFlagReads { get; init; }
    /// <summary><c>do ... while</c> statements.</summary>
    public required int DoLoops { get; init; }
    /// <summary><c>while (true)</c> statements; other <c>while</c> statements are not counted.</summary>
    public required int WhileTrueLoops { get; init; }
    /// <summary><c>if</c> statements; an <c>else if</c> counts as a second one.</summary>
    public required int IfStatements { get; init; }
    /// <summary><c>break</c> statements, in loops and in <c>switch</c> sections.</summary>
    public required int BreakStatements { get; init; }
    /// <summary><c>continue</c> statements.</summary>
    public required int ContinueStatements { get; init; }
    public required int Warnings { get; init; }
    public required ImmutableArray<string> WarningIds { get; init; }

    // Every value is written as name=value, so a row can be read without counting columns. Fields are padded to fixed
    // widths so rows stay aligned regardless of the order fixtures are appended in; the last field (warning ids) is
    // left unpadded because it is free text of varying length.
    private const int FixtureFieldWidth = 56;
    private const int FieldWidth = 15;

    /// <summary>The metrics as one line of <c>name=value</c> fields, in a fixed order.</summary>
    public string ToLine() {
        string warningIds = WarningIds.IsDefaultOrEmpty ? "" : string.Join(",", WarningIds);
        (string Name, string Value)[] fields = [
            ("fixture", Fixture),
            ("methods", Methods.ToString()),
            ("lines", Lines.ToString()),
            ("labels", Labels.ToString()),
            ("gotos", GotoStatements.ToString()),
            ("gotoTgts", DistinctGotoTargets.ToString()),
            ("extEvents", CheckExternalEvents.ToString()),
            ("specGuard", VerifySpeculativeEntryOrFail.ToString()),
            ("untested", FailAsUntested.ToString()),
            ("unchecked", UncheckedExpressions.ToString()),
            ("casts", Casts.ToString()),
            ("tries", TryStatements.ToString()),
            ("aluCalls", AluCalls.ToString()),
            ("flagReads", StatusFlagReads.ToString()),
            ("doLoops", DoLoops.ToString()),
            ("whileTrue", WhileTrueLoops.ToString()),
            ("ifs", IfStatements.ToString()),
            ("breaks", BreakStatements.ToString()),
            ("continues", ContinueStatements.ToString()),
            ("warnings", Warnings.ToString()),
            ("warningIds", warningIds)
        ];
        return string.Concat(fields.Select((field, index) => Pad($"{field.Name}={field.Value}", WidthOf(index, fields.Length))));
    }

    private static int WidthOf(int index, int count) {
        if (index == 0) {
            return FixtureFieldWidth;
        }
        if (index == count - 1) {
            return 0;
        }
        return FieldWidth;
    }

    private static string Pad(string value, int width) {
        if (width == 0) {
            return value;
        }
        // One trailing space guarantees a separator even when a value is wider than its column.
        return value.PadRight(width - 1) + " ";
    }
}
