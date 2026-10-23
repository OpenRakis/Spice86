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
    private static readonly HashSet<string> AluTargetNames = new(StringComparer.Ordinal) { "Alu8", "Alu16", "Alu32" };
    private static readonly HashSet<string> StatusFlagNames = new(StringComparer.Ordinal) {
        "CarryFlag", "ParityFlag", "AuxiliaryFlag", "ZeroFlag", "SignFlag", "OverflowFlag"
    };

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
            DistinctGotoTargets = walker.DistinctGotoTargetsPerMethod,
            CheckExternalEvents = walker.CheckExternalEvents,
            VerifySpeculativeEntryOrFail = walker.VerifySpeculativeEntryOrFail,
            FailAsUntested = walker.FailAsUntested,
            UncheckedExpressions = walker.UncheckedExpressions,
            Casts = walker.Casts,
            TryStatements = walker.TryStatements,
            AluCalls = walker.AluCalls,
            StatusFlagReads = walker.StatusFlagReads,
            Warnings = warnings.Length,
            WarningIds = warningIds
        };
    }

    private sealed class MetricsWalker : CSharpSyntaxWalker {
        public int Methods { get; private set; }
        public int Labels { get; private set; }
        public int GotoStatements { get; private set; }
        public HashSet<string> GotoTargets { get; } = new(StringComparer.Ordinal);
        public int DistinctGotoTargetsPerMethod { get; private set; }
        public int CheckExternalEvents { get; private set; }
        public int VerifySpeculativeEntryOrFail { get; private set; }
        public int FailAsUntested { get; private set; }
        public int UncheckedExpressions { get; private set; }
        public int Casts { get; private set; }
        public int TryStatements { get; private set; }
        public int AluCalls { get; private set; }
        public int StatusFlagReads { get; private set; }

        public override void VisitMethodDeclaration(MethodDeclarationSyntax node) {
            Methods++;
            GotoTargets.Clear();
            base.VisitMethodDeclaration(node);
            DistinctGotoTargetsPerMethod += GotoTargets.Count;
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
            if (node.Expression is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax target }
                && AluTargetNames.Contains(target.Identifier.ValueText)) {
                AluCalls++;
            }
            base.VisitInvocationExpression(node);
        }

        public override void VisitIdentifierName(IdentifierNameSyntax node) {
            if (StatusFlagNames.Contains(node.Identifier.ValueText) && !IsAssignmentTarget(node)) {
                StatusFlagReads++;
            }
            base.VisitIdentifierName(node);
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

        private static bool IsAssignmentTarget(IdentifierNameSyntax node) {
            ExpressionSyntax target = node;
            if (node.Parent is MemberAccessExpressionSyntax memberAccess && memberAccess.Name == node) {
                target = memberAccess;
            }
            return target.Parent is AssignmentExpressionSyntax assignment && assignment.Left == target;
        }
    }
}
