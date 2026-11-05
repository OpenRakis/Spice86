namespace Spice86.Tests;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;

/// <summary>
/// Computes <see cref="GeneratedCodeMetrics"/> from a parsed syntax tree and compilation diagnostics.
/// Counting is done on the syntax tree rather than with text regexes so it stays robust to the formatting
/// changes later phases make to the generated output.
/// </summary>
internal static class GeneratedCodeMetricsCollector {
    private const string CheckExternalEventsName = "CheckExternalEvents";
    private const string InterruptCallName = "InterruptCall";
    private const string InterruptCallReturnedElsewhereName = "InterruptCallReturnedElsewhere";
    private static readonly HashSet<string> InterruptServiceGroupNames = new(StringComparer.Ordinal) {
        "Video", "Equipment", "MemorySize", "Disk", "Serial", "SystemBios", "Keyboard",
        "Printer", "Clock", "UserTimer", "Dos", "CriticalError", "DosIdle", "DosNetwork",
        "DosMultiplex", "Mouse", "Ems", "Rtc"
    };
    private const string VerifySpeculativeEntryOrFailName = "VerifySpeculativeEntryOrFail";
    private const string VerifySpeculativeRunOrFailName = "VerifySpeculativeRunOrFail";
    private const string FailAsUntestedName = "FailAsUntested";
    private static readonly HashSet<string> AluTargetNames = new(StringComparer.Ordinal) { "Alu8", "Alu16", "Alu32" };
    private static readonly HashSet<string> StatusFlagNames = new(StringComparer.Ordinal) {
        "CarryFlag", "ParityFlag", "AuxiliaryFlag", "ZeroFlag", "SignFlag", "OverflowFlag"
    };
    private static readonly HashSet<string> TypedFlagParameterNames = new(StringComparer.Ordinal) {
        "carryFlag", "parityFlag", "auxiliaryFlag", "zeroFlag", "signFlag", "overflowFlag"
    };
    private static readonly HashSet<string> StackMethodNames = new(StringComparer.Ordinal) {
        "Push16", "Push32", "Pop16", "Pop32"
    };
    private static readonly HashSet<string> MemoryIndexerNames = new(StringComparer.Ordinal) {
        "UInt8", "UInt16", "UInt32", "Int8", "Int16", "Int32",
        "SsUInt8", "SsUInt16", "SsUInt32", "SsInt8", "SsInt16", "SsInt32"
    };
    private static readonly HashSet<string> GlobalsClassNames = new(StringComparer.Ordinal) {
        "DsGlobals", "EsGlobals", "CsGlobals", "FsGlobals", "GsGlobals"
    };
    private static readonly System.Text.RegularExpressions.Regex LocationClassName = new(
        @"^(Segment[0-9A-F]{4}|Image(Minus)?[0-9A-F]{4})Globals$");
    private static readonly HashSet<string> TableClassNames = new(StringComparer.Ordinal) {
        "DsTables", "EsTables", "CsTables", "FsTables", "GsTables"
    };
    private static readonly System.Text.RegularExpressions.Regex LocationTableClassName = new(
        @"^(Segment[0-9A-F]{4}|Image(Minus)?[0-9A-F]{4})Tables$");
    private static readonly HashSet<string> RegisterLocalNames = new(StringComparer.Ordinal) {
        "ax", "cx", "dx", "bx", "bp", "si", "di", "es", "ds",
        "al", "cl", "dl", "bl", "ah", "ch", "dh", "bh",
        "eax", "ecx", "edx", "ebx", "ebp", "esi", "edi"
    };
    private static readonly HashSet<string> RegisterNames = new(StringComparer.Ordinal) {
        "AX", "CX", "DX", "BX", "SP", "BP", "SI", "DI",
        "ES", "CS", "SS", "DS", "FS", "GS",
        "AL", "CL", "DL", "BL", "AH", "CH", "DH", "BH",
        "EAX", "ECX", "EDX", "EBX", "ESP", "EBP", "ESI", "EDI"
    };

    /// <summary>The register local name <paramref name="name"/> starts from, trailing digits removed
    /// (<c>cx1</c> gives <c>cx</c>).</summary>
    private static string RegisterLocalBase(string name) {
        int end = name.Length;
        while (end > 0 && char.IsAsciiDigit(name[end - 1])) {
            end--;
        }
        return name[..end];
    }

    /// <summary>Whether <paramref name="name"/> is a numbered register local (<c>cx1</c>, <c>ax2</c>).</summary>
    private static bool IsNumberedRegisterLocal(string name) =>
        name.Length > 0 && char.IsAsciiDigit(name[^1])
        && RegisterLocalNames.Contains(RegisterLocalBase(name));
    private static readonly HashSet<SyntaxKind> CompoundAssignmentKinds = new() {
        SyntaxKind.AddAssignmentExpression, SyntaxKind.SubtractAssignmentExpression,
        SyntaxKind.AndAssignmentExpression, SyntaxKind.OrAssignmentExpression,
        SyntaxKind.ExclusiveOrAssignmentExpression, SyntaxKind.LeftShiftAssignmentExpression,
        SyntaxKind.RightShiftAssignmentExpression
    };

    /// <summary>Sums the metrics over every syntax tree of one generated program; per-file invariants
    /// (for example every label being a goto target) are computed on the merged walk, which is the
    /// same as per-tree here because every counted construct is local to one method.</summary>
    public static GeneratedCodeMetrics Collect(string fixture, IReadOnlyList<SyntaxTree> trees, ImmutableArray<Diagnostic> diagnostics) {
        MetricsWalker walker = new();
        int lines = 0;
        int dataComments = 0;
        foreach (SyntaxTree tree in trees) {
            SyntaxNode root = tree.GetRoot();
            walker.Visit(root);
            lines += tree.GetText().Lines.Count;
            dataComments += root.DescendantTrivia()
                .Count(trivia => trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
                    && trivia.ToString().TrimStart().StartsWith("// DOS ", StringComparison.Ordinal));
        }

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
            DoLoops = walker.DoLoops,
            WhileTrueLoops = walker.WhileTrueLoops,
            ForLoops = walker.ForLoops,
            IfStatements = walker.IfStatements,
            BreakStatements = walker.BreakStatements,
            ContinueStatements = walker.ContinueStatements,
            StackCalls = walker.StackCalls,
            FlagAssignments = walker.FlagAssignments,
            MethodVariables = walker.MethodVariables,
            CompoundAssignments = walker.CompoundAssignments,
            TupleSwaps = walker.TupleSwaps,
            MemoryAccesses = walker.MemoryAccesses,
            GlobalAccesses = walker.GlobalAccesses,
            LocationAccesses = walker.LocationAccesses,
            TableAccesses = walker.TableAccesses,
            DataComments = dataComments,
            InterruptCalls = walker.InterruptCalls,
            InterruptCallReturnedElsewhere = walker.InterruptCallReturnedElsewhere,
            TypedServiceCalls = walker.TypedServiceCalls,
            StringServiceCalls = walker.StringServiceCalls,
            RegisterLocals = walker.RegisterLocals,
            RegisterLocalsMulti = walker.RegisterLocalsMulti,
            SignedLocals = walker.SignedLocals,
            WriteBacks = walker.WriteBacks,
            FrameAccesses = walker.FrameAccesses,
            SavedPairs = walker.SavedPairs,
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
        public int DoLoops { get; private set; }
        public int WhileTrueLoops { get; private set; }
        public int ForLoops { get; private set; }
        public int IfStatements { get; private set; }
        public int BreakStatements { get; private set; }
        public int ContinueStatements { get; private set; }
        public int StackCalls { get; private set; }
        public int FlagAssignments { get; private set; }
        public int MethodVariables { get; private set; }
        public int CompoundAssignments { get; private set; }
        public int TupleSwaps { get; private set; }
        public int MemoryAccesses { get; private set; }
        public int GlobalAccesses { get; private set; }
        public int LocationAccesses { get; private set; }
        public int TableAccesses { get; private set; }
        public int InterruptCalls { get; private set; }
        public int InterruptCallReturnedElsewhere { get; private set; }
        public int TypedServiceCalls { get; private set; }
        public int StringServiceCalls { get; private set; }
        public int RegisterLocals { get; private set; }
        public int RegisterLocalsMulti { get; private set; }
        public int SignedLocals { get; private set; }
        public int WriteBacks { get; private set; }
        public int FrameAccesses { get; private set; }
        public int SavedPairs { get; private set; }

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
            } else if (name == VerifySpeculativeEntryOrFailName
                || name == VerifySpeculativeRunOrFailName) {
                VerifySpeculativeEntryOrFail++;
            } else if (name == FailAsUntestedName) {
                FailAsUntested++;
            } else if (name == InterruptCallName) {
                InterruptCalls++;
            } else if (name == InterruptCallReturnedElsewhereName) {
                InterruptCallReturnedElsewhere++;
            }
            if (node.Expression is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax target } member) {
                if (InterruptServiceGroupNames.Contains(target.Identifier.ValueText)) {
                    TypedServiceCalls++;
                    if (node.ArgumentList.Arguments.Count > 0
                        && node.ArgumentList.Arguments[^1].Expression
                            .IsKind(SyntaxKind.StringLiteralExpression)) {
                        StringServiceCalls++;
                    }
                }
                if (AluTargetNames.Contains(target.Identifier.ValueText)) {
                    AluCalls++;
                } else if (target.Identifier.ValueText == "Stack"
                    && StackMethodNames.Contains(member.Name.Identifier.ValueText)) {
                    StackCalls++;
                }
            }
            base.VisitInvocationExpression(node);
        }

        public override void VisitAssignmentExpression(AssignmentExpressionSyntax node) {
            if (node.Left is IdentifierNameSyntax flag && StatusFlagNames.Contains(flag.Identifier.ValueText)) {
                FlagAssignments++;
            }
            if (CompoundAssignmentKinds.Contains(node.Kind())) {
                CompoundAssignments++;
            }
            if (node.Left is TupleExpressionSyntax) {
                TupleSwaps++;
            }
            if (node.Left is IdentifierNameSyntax writeBackTarget
                && RegisterNames.Contains(writeBackTarget.Identifier.ValueText)
                && node.Right is IdentifierNameSyntax local
                && RegisterLocalBase(local.Identifier.ValueText)
                    == writeBackTarget.Identifier.ValueText.ToLowerInvariant()) {
                WriteBacks++;
            }
            base.VisitAssignmentExpression(node);
        }

        public override void VisitElementAccessExpression(ElementAccessExpressionSyntax node) {
            if (node.Expression is IdentifierNameSyntax identifier
                && MemoryIndexerNames.Contains(identifier.Identifier.ValueText)) {
                MemoryAccesses++;
            }
            base.VisitElementAccessExpression(node);
        }

        public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node) {
            if (node.Expression is IdentifierNameSyntax identifier
                && node.Name is IdentifierNameSyntax) {
                string className = identifier.Identifier.ValueText;
                if (GlobalsClassNames.Contains(className) || LocationClassName.IsMatch(className)) {
                    MemoryAccesses++;
                    GlobalAccesses++;
                    if (LocationClassName.IsMatch(className)) {
                        LocationAccesses++;
                    }
                } else if (TableClassNames.Contains(className) || LocationTableClassName.IsMatch(className)) {
                    MemoryAccesses++;
                    TableAccesses++;
                    if (LocationTableClassName.IsMatch(className)) {
                        LocationAccesses++;
                    }
                } else if (className == "frame") {
                    MemoryAccesses++;
                    FrameAccesses++;
                }
            }
            base.VisitMemberAccessExpression(node);
        }

        public override void VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node) {
            if (node.Parent is BlockSyntax { Parent: MethodDeclarationSyntax }) {
                MethodVariables++;
                if (node.Declaration.Variables.Any(variable =>
                        RegisterLocalNames.Contains(variable.Identifier.ValueText)
                        || IsNumberedRegisterLocal(variable.Identifier.ValueText))) {
                    RegisterLocals++;
                    if (node.Declaration.Variables.Any(variable =>
                            IsNumberedRegisterLocal(variable.Identifier.ValueText))) {
                        RegisterLocalsMulti++;
                    }
                    if (node.Declaration.Type is PredefinedTypeSyntax { Keyword.ValueText: "short" or "sbyte" or "int" }) {
                        SignedLocals++;
                    }
                }
                SavedPairs += node.Declaration.Variables.Count(variable =>
                    variable.Identifier.ValueText.StartsWith("saved", StringComparison.Ordinal));
            }
            base.VisitLocalDeclarationStatement(node);
        }

        public override void VisitIdentifierName(IdentifierNameSyntax node) {
            if (IsCountedFlagName(node) && !IsAssignmentTarget(node)) {
                StatusFlagReads++;
            }
            base.VisitIdentifierName(node);
        }

        /// <summary>Whether <paramref name="node"/> names a flag the IR actually reads: a status
        /// flag name that is not a generated flag use, or a typed flag parameter that is not the
        /// entry assignment to its flag.</summary>
        private static bool IsCountedFlagName(IdentifierNameSyntax node) {
            string name = node.Identifier.ValueText;
            return (StatusFlagNames.Contains(name) && !IsGeneratedFlagUse(node))
                || (TypedFlagParameterNames.Contains(name) && !IsFlagParameterEntryAssignment(node));
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

        public override void VisitDoStatement(DoStatementSyntax node) {
            DoLoops++;
            base.VisitDoStatement(node);
        }

        public override void VisitForStatement(ForStatementSyntax node) {
            ForLoops++;
            base.VisitForStatement(node);
        }

        public override void VisitWhileStatement(WhileStatementSyntax node) {
            if (node.Condition.IsKind(SyntaxKind.TrueLiteralExpression)) {
                WhileTrueLoops++;
            }
            base.VisitWhileStatement(node);
        }

        public override void VisitIfStatement(IfStatementSyntax node) {
            IfStatements++;
            base.VisitIfStatement(node);
        }

        public override void VisitBreakStatement(BreakStatementSyntax node) {
            BreakStatements++;
            base.VisitBreakStatement(node);
        }

        public override void VisitContinueStatement(ContinueStatementSyntax node) {
            ContinueStatements++;
            base.VisitContinueStatement(node);
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

        /// <summary>Whether <paramref name="node"/> is a typed flag parameter in the entry
        /// assignment <c>Flag = parameter;</c> the signature surface prints before the body: a
        /// flag write, not a flag read the IR performs.</summary>
        private static bool IsFlagParameterEntryAssignment(IdentifierNameSyntax node) =>
            node.Parent is AssignmentExpressionSyntax assignment
            && assignment.Right == node
            && assignment.Left is IdentifierNameSyntax flag
            && StatusFlagNames.Contains(flag.Identifier.ValueText);

        /// <summary>Whether <paramref name="node"/> is a flag name the typed-signature surface
        /// prints without a matching IR flag read: a flag output in a <c>return</c>, a flag
        /// argument of a typed call's <c>() => f(...)</c> lambda, or a flag argument of an
        /// <c>_Asm</c> entry wrapper's call.</summary>
        private static bool IsGeneratedFlagUse(IdentifierNameSyntax node) =>
            node.Ancestors().Any(ancestor => ancestor switch {
                ReturnStatementSyntax or SimpleLambdaExpressionSyntax or ParenthesizedLambdaExpressionSyntax => true,
                MethodDeclarationSyntax method => method.Identifier.ValueText.EndsWith("_Asm", StringComparison.Ordinal),
                _ => false
            });

        private static bool IsAssignmentTarget(IdentifierNameSyntax node) {
            SyntaxNode target = node;
            while (target.Parent is MemberAccessExpressionSyntax memberAccess && memberAccess.Name == target
                   || target.Parent is ArgumentSyntax
                   || target.Parent is TupleExpressionSyntax) {
                target = target.Parent;
            }
            return target.Parent is AssignmentExpressionSyntax assignment && assignment.Left == target;
        }
    }
}
