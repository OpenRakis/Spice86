namespace Spice86.Tests;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

using System.Collections.Immutable;

/// <summary>
/// Computes <see cref="GeneratedCodeMetrics"/> from a parsed syntax tree and compilation diagnostics.
/// Counting is done on the syntax tree rather than with text regexes so it stays robust to the formatting
/// changes later phases make to the generated output.
/// </summary>
internal static class GeneratedCodeMetricsCollector {
    private const string CheckExternalEventsName = "CheckExternalEvents";
    private const string VerifySpeculativeEntryOrFailName = "VerifySpeculativeEntryOrFail";
    private const string FailAsUntestedName = "FailAsUntested";

    public static GeneratedCodeMetrics Collect(string fixture, SyntaxTree tree, ImmutableArray<Diagnostic> diagnostics) {
        SyntaxNode root = tree.GetRoot();
        MetricsWalker walker = new();
        walker.Visit(root);

        SourceText text = tree.GetText();
        int lines = text.Lines.Count;

        ImmutableArray<Diagnostic> warnings = diagnostics
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning)
            .ToImmutableArray();
        ImmutableArray<string> warningIds = warnings
            .Select(diagnostic => diagnostic.Id)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToImmutableArray();

        return new GeneratedCodeMetrics {
            Fixture = fixture,
            Methods = walker.Methods,
            Lines = lines,
            Labels = walker.Labels,
            GotoStatements = walker.GotoStatements,
            DistinctGotoTargets = walker.GotoTargets.Count,
            CheckExternalEvents = walker.CheckExternalEvents,
            VerifySpeculativeEntryOrFail = walker.VerifySpeculativeEntryOrFail,
            FailAsUntested = walker.FailAsUntested,
            UncheckedExpressions = walker.UncheckedExpressions,
            Casts = walker.Casts,
            TryStatements = walker.TryStatements,
            Warnings = warnings.Length,
            WarningIds = warningIds
        };
    }

    private sealed class MetricsWalker : CSharpSyntaxWalker {
        public int Methods { get; private set; }
        public int Labels { get; private set; }
        public int GotoStatements { get; private set; }
        public HashSet<string> GotoTargets { get; } = new(StringComparer.Ordinal);
        public int CheckExternalEvents { get; private set; }
        public int VerifySpeculativeEntryOrFail { get; private set; }
        public int FailAsUntested { get; private set; }
        public int UncheckedExpressions { get; private set; }
        public int Casts { get; private set; }
        public int TryStatements { get; private set; }

        public override void VisitMethodDeclaration(MethodDeclarationSyntax node) {
            Methods++;
            base.VisitMethodDeclaration(node);
        }

        public override void VisitLabeledStatement(LabeledStatementSyntax node) {
            Labels++;
            base.VisitLabeledStatement(node);
        }

        public override void VisitGotoStatement(GotoStatementSyntax node) {
            GotoStatements++;
            if (node.Expression is IdentifierNameSyntax identifier) {
                GotoTargets.Add(identifier.Identifier.ValueText);
            }
            base.VisitGotoStatement(node);
        }

        public override void VisitInvocationExpression(InvocationExpressionSyntax node) {
            string? name = GetInvokedName(node.Expression);
            if (name == CheckExternalEventsName) {
                CheckExternalEvents++;
            } else if (name == VerifySpeculativeEntryOrFailName) {
                VerifySpeculativeEntryOrFail++;
            } else if (name == FailAsUntestedName) {
                FailAsUntested++;
            }
            base.VisitInvocationExpression(node);
        }

        public override void VisitCheckedExpression(CheckedExpressionSyntax node) {
            if (node.Keyword.IsKind(SyntaxKind.UncheckedKeyword)) {
                UncheckedExpressions++;
            }
            base.VisitCheckedExpression(node);
        }

        public override void VisitCastExpression(CastExpressionSyntax node) {
            Casts++;
            base.VisitCastExpression(node);
        }

        public override void VisitTryStatement(TryStatementSyntax node) {
            TryStatements++;
            base.VisitTryStatement(node);
        }

        private static string? GetInvokedName(ExpressionSyntax expression) {
            if (expression is IdentifierNameSyntax identifier) {
                return identifier.Identifier.ValueText;
            }
            if (expression is MemberAccessExpressionSyntax memberAccess) {
                return memberAccess.Name.Identifier.ValueText;
            }
            return null;
        }
    }
}
