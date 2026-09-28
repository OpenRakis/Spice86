namespace Spice86.Tests;

using System.Collections.Immutable;
using System.Linq;

/// <summary>
/// Readability metrics for a single generated-code fixture, computed from the Roslyn syntax tree and the
/// compilation diagnostics. Used by the code-generator quality harness to track how later phases change the
/// generated output. No assertions are made on these numbers yet; they run dark and are recorded to a file.
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
    public required int Warnings { get; init; }
    public required ImmutableArray<string> WarningIds { get; init; }

    // Fixed column widths so rows stay aligned regardless of the order fixtures are appended in. The
    // last column (warning ids) is left unpadded because it is free text of varying length.
    private const int FixtureWidth = 48;
    private const int NumberWidth = 9;

    private static readonly (string Header, int Width)[] Columns = [
        ("fixture", FixtureWidth),
        ("methods", NumberWidth),
        ("lines", NumberWidth),
        ("labels", NumberWidth),
        ("gotos", NumberWidth),
        ("gotoTgts", NumberWidth),
        ("extEvents", NumberWidth),
        ("specGuard", NumberWidth),
        ("untested", NumberWidth),
        ("unchecked", NumberWidth),
        ("casts", NumberWidth),
        ("tries", NumberWidth),
        ("warnings", NumberWidth),
        ("warningIds", 0)
    ];

    public static string Header() {
        return string.Concat(Columns.Select(column => Pad(column.Header, column.Width)));
    }

    public string ToLine() {
        string warningIds = WarningIds.IsDefaultOrEmpty ? "" : string.Join(",", WarningIds);
        string[] values = [
            Fixture,
            Methods.ToString(),
            Lines.ToString(),
            Labels.ToString(),
            GotoStatements.ToString(),
            DistinctGotoTargets.ToString(),
            CheckExternalEvents.ToString(),
            VerifySpeculativeEntryOrFail.ToString(),
            FailAsUntested.ToString(),
            UncheckedExpressions.ToString(),
            Casts.ToString(),
            TryStatements.ToString(),
            Warnings.ToString(),
            warningIds
        ];
        return string.Concat(values.Select((value, index) => Pad(value, Columns[index].Width)));
    }

    private static string Pad(string value, int width) {
        if (width == 0) {
            return value;
        }
        // One trailing space guarantees a separator even when a value is wider than its column.
        return value.PadRight(width - 1) + " ";
    }
}
