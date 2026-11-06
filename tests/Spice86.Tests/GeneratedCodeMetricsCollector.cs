namespace Spice86.Tests;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.Devices.Timer;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.InterruptHandlers.Dos;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.ReverseEngineer;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
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
        "ES", "CS", "SS", "DS", "FS", "GS", "IP",
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

    public static GeneratedCodeMetrics Collect(string fixture, GeneratedCompilation compilation) {
        EnsureBinding(compilation);
        return Collect(fixture, compilation, compilation.SyntaxTrees.Select(tree => tree.GetRoot()).ToArray());
    }

    public static IReadOnlyDictionary<string, GeneratedCodeMetrics> CollectMethods(
        string fixture, GeneratedCompilation compilation) {
        EnsureBinding(compilation);
        SortedDictionary<string, GeneratedCodeMetrics> methods = new(StringComparer.Ordinal);
        for (int treeIndex = 0; treeIndex < compilation.SyntaxTrees.Count; treeIndex++) {
            SyntaxTree tree = compilation.SyntaxTrees[treeIndex];
            SemanticModel model = compilation.Compilation.GetSemanticModel(tree);
            foreach (BaseMethodDeclarationSyntax method in tree.GetRoot().DescendantNodes()
                         .OfType<BaseMethodDeclarationSyntax>()) {
                IMethodSymbol symbol = model.GetDeclaredSymbol(method)
                    ?? throw new InvalidOperationException($"Unbound method in {fixture}.");
                string key = symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
                methods.Add(key, Collect(fixture, compilation, [method]) with {
                    SourceLocation = $"tree={treeIndex},line={method.GetLocation().GetLineSpan().StartLinePosition.Line + 1}"
                });
            }
        }
        return methods;
    }

    private static void EnsureBinding(GeneratedCompilation compilation) {
        Diagnostic[] errors = compilation.Compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        if (errors.Length != 0) {
            throw new InvalidOperationException("Cannot measure unbound generated source:" + Environment.NewLine
                + string.Join(Environment.NewLine, errors.Select(error => error.ToString())));
        }
    }

    /// <summary>Sums the metrics over every syntax tree of one generated program; per-file invariants
    /// (for example every label being a goto target) are computed on the merged walk, which is the
    /// same as per-tree here because every counted construct is local to one method.</summary>
    private static GeneratedCodeMetrics Collect(string fixture, GeneratedCompilation compilation, IReadOnlyList<SyntaxNode> roots) {
        MetricsWalker walker = new(compilation.Compilation);
        int lines = 0;
        int dataComments = 0;
        foreach (SyntaxNode root in roots) {
            walker.Visit(root);
            if (root is CompilationUnitSyntax) {
                lines += root.SyntaxTree.GetText().Lines.Count;
            } else {
                lines += root.GetLocation().GetLineSpan().EndLinePosition.Line
                    - root.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            }
            dataComments += root.DescendantTrivia()
                .Count(trivia => trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
                    && trivia.ToString().TrimStart().StartsWith("// DOS ", StringComparison.Ordinal));
        }

        bool wholeFixture = roots.All(root => root is CompilationUnitSyntax);
        ImmutableArray<Diagnostic> warnings = compilation.EmitResult.Diagnostics
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning)
            .Where(diagnostic => wholeFixture || roots.Any(root => diagnostic.Location.SourceTree == root.SyntaxTree
                && root.FullSpan.Contains(diagnostic.Location.SourceSpan)))
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
            ServiceCallsByReceiver = walker.ServiceCallsByReceiver,
            StringServiceCalls = walker.StringServiceCalls,
            RegisterLocals = walker.RegisterLocals,
            RegisterLocalsMulti = walker.RegisterLocalsMulti,
            SignedLocals = walker.SignedLocals,
            WriteBacks = walker.WriteBacks,
            FrameAccesses = walker.FrameAccesses,
            SavedPairs = walker.SavedPairs,
            DeviceCallsByReceiver = walker.DeviceCallsByReceiver,
            ServiceCallsByOperation = walker.ServiceCallsByOperation,
            DeviceCallsByOperation = walker.DeviceCallsByOperation,
            PortWrapperCalls = walker.PortWrapperCalls,
            RawIoCalls = walker.RawIoCalls,
            RegisterReads = walker.RegisterReads,
            RegisterWrites = walker.RegisterWrites,
            PlainRegisterAssignments = walker.PlainRegisterAssignments,
            LocalVariables = walker.Locals.Count,
            LocalReads = walker.LocalReads,
            LocalWrites = walker.LocalWrites,
            BookkeepingCalls = walker.BookkeepingCalls,
            Statements = walker.Statements,
            Warnings = warnings.Length,
            WarningIds = warningIds
        };
    }

    private sealed class MetricsWalker(CSharpCompilation compilation) : CSharpSyntaxWalker {
        private readonly IReadOnlyDictionary<SyntaxTree, SemanticModel> _models =
            compilation.SyntaxTrees.ToDictionary(tree => tree, tree => compilation.GetSemanticModel(tree));
        public HashSet<ISymbol> Locals { get; } = new(SymbolEqualityComparer.Default);
        public Dictionary<string, int> DeviceCallsByReceiver { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> ServiceCallsByOperation { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> DeviceCallsByOperation { get; } = new(StringComparer.Ordinal);
        public int PortWrapperCalls { get; private set; }
        public int RawIoCalls { get; private set; }
        public int RegisterReads { get; private set; }
        public int RegisterWrites { get; private set; }
        public int PlainRegisterAssignments { get; private set; }
        public int LocalReads { get; private set; }
        public int LocalWrites { get; private set; }
        public int BookkeepingCalls { get; private set; }
        public int Statements { get; private set; }
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
        public Dictionary<string, int> ServiceCallsByReceiver { get; } = new(StringComparer.Ordinal);
        public int StringServiceCalls { get; private set; }
        public int RegisterLocals { get; private set; }
        public int RegisterLocalsMulti { get; private set; }
        public int SignedLocals { get; private set; }
        public int WriteBacks { get; private set; }
        public int FrameAccesses { get; private set; }
        public int SavedPairs { get; private set; }

        public override void Visit(SyntaxNode? node) {
            if (node is null) {
                return;
            }
            SemanticModel model = _models[node.SyntaxTree];
            if (node is InvocationExpressionSyntax && model.GetOperation(node) is INameOfOperation) {
                return;
            }
            if (node is StatementSyntax and not (BlockSyntax or LabeledStatementSyntax
                or EmptyStatementSyntax or LocalFunctionStatementSyntax)) {
                Statements++;
            }
            CountLocalDeclaration(node, model);
            if (node is IdentifierNameSyntax or MemberAccessExpressionSyntax
                && !(node.Parent is MemberAccessExpressionSyntax member && member.Name == node)) {
                CountValueAccess(model.GetOperation(node));
            }
            base.Visit(node);
        }

        private void CountLocalDeclaration(SyntaxNode node, SemanticModel model) {
            if (node is not (VariableDeclaratorSyntax or SingleVariableDesignationSyntax
                or ForEachStatementSyntax or CatchDeclarationSyntax)) {
                return;
            }
            if (model.GetDeclaredSymbol(node) is not ILocalSymbol local || !Locals.Add(local)) {
                return;
            }
            if (node is not VariableDeclaratorSyntax variable || variable.Initializer is not null) {
                LocalWrites++;
            }
        }

        private bool IsType(ITypeSymbol? symbol, Type type) =>
            SymbolEqualityComparer.Default.Equals(symbol, compilation.GetTypeByMetadataName(
                type.FullName ?? throw new InvalidOperationException($"Unnamed type {type}.")));

        private bool IsRegister(IPropertySymbol property) =>
            RegisterNames.Contains(property.Name)
            && (IsType(property.ContainingType, typeof(CSharpOverrideHelper))
                || IsType(property.ContainingType, typeof(State)));

        private void CountValueAccess(IOperation? operation) {
            if (operation is null) {
                return;
            }
            bool register = operation is IPropertyReferenceOperation property && IsRegister(property.Property);
            if (!register && operation is not ILocalReferenceOperation) {
                return;
            }
            (bool read, bool write) = AccessKind(operation);
            if (register) {
                RegisterReads += read ? 1 : 0;
                RegisterWrites += write ? 1 : 0;
            } else {
                LocalReads += read ? 1 : 0;
                LocalWrites += write ? 1 : 0;
            }
        }

        private static (bool Read, bool Write) AccessKind(IOperation operation) {
            IOperation target = operation;
            while (target.Parent is ITupleOperation or IConversionOperation
                or IParenthesizedOperation or IDeclarationExpressionOperation) {
                target = target.Parent;
            }
            return target.Parent switch {
                ICompoundAssignmentOperation assignment when ReferenceEquals(assignment.Target, target) => (true, true),
                IIncrementOrDecrementOperation => (true, true),
                IAssignmentOperation assignment when ReferenceEquals(assignment.Target, target) => (false, true),
                IArgumentOperation { Parameter.RefKind: RefKind.Out } => (false, true),
                IArgumentOperation { Parameter.RefKind: RefKind.Ref } => (true, true),
                _ => (true, false)
            };
        }

        public override void VisitMethodDeclaration(MethodDeclarationSyntax node) {
            Methods++;
            GotoTargets.Clear();
            base.VisitMethodDeclaration(node);
            DistinctGotoTargetsPerMethod += GotoTargets.Count;
        }

        public override void VisitConstructorDeclaration(ConstructorDeclarationSyntax node) {
            GotoTargets.Clear();
            base.VisitConstructorDeclaration(node);
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
            }
            CountOperationCall(node);
            if (node.Expression is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax target } member) {
                if (AluTargetNames.Contains(target.Identifier.ValueText)) {
                    AluCalls++;
                } else if (target.Identifier.ValueText == "Stack"
                    && StackMethodNames.Contains(member.Name.Identifier.ValueText)) {
                    StackCalls++;
                }
            }
            base.VisitInvocationExpression(node);
        }

        private void CountOperationCall(InvocationExpressionSyntax node) {
            if (_models[node.SyntaxTree].GetOperation(node) is not IInvocationOperation invocation) {
                return;
            }
            IMethodSymbol method = invocation.TargetMethod;
            if (IsType(method.ContainingType, typeof(CSharpOverrideHelper))) {
                if (method.Name == InterruptCallName) {
                    InterruptCalls++;
                } else if (method.Name == InterruptCallReturnedElsewhereName) {
                    InterruptCallReturnedElsewhere++;
                } else if (method.Name is "In8" or "In16" or "In32" or "Out8" or "Out16" or "Out32") {
                    RawIoCalls++;
                }
            }
            if (IsType(method.ContainingType, typeof(IOPortDispatcher))
                && method.Name is "ReadByte" or "ReadWord" or "ReadDWord" or "WriteByte" or "WriteWord" or "WriteDWord") {
                RawIoCalls++;
            }
            if (HasAttribute(method, typeof(PortWrapperAttribute))) {
                PortWrapperCalls++;
                return;
            }
            if (invocation.Instance is not IPropertyReferenceOperation receiver
                || !IsType(receiver.Property.ContainingType, typeof(CSharpOverrideHelper))) {
                return;
            }
            string receiverName = receiver.Property.Name;
            if (IsType(method.ContainingType, typeof(DosInt21Handler))
                && method.Name == nameof(DosInt21Handler.RecordCallerStack)) {
                BookkeepingCalls++;
            } else if (IsServiceImplementation(receiver.Property.Type, method)) {
                Increment(ServiceCallsByReceiver, receiverName);
                Increment(ServiceCallsByOperation, $"{receiverName}.{method.Name}");
                if (node.ArgumentList.DescendantNodes().OfType<LiteralExpressionSyntax>()
                    .Any(literal => literal.IsKind(SyntaxKind.StringLiteralExpression))) {
                    StringServiceCalls++;
                }
            } else if (IsDeviceOperation(receiverName, method)) {
                Increment(DeviceCallsByReceiver, receiverName);
                Increment(DeviceCallsByOperation, $"{receiverName}.{method.Name}");
            }
        }

        private bool IsDeviceOperation(string receiverName, IMethodSymbol method) {
            if (DeviceOperationRegistry.ReceiverType(receiverName) is null) {
                return false;
            }
            if (HasAttribute(method, typeof(PortSequenceStepAttribute))) {
                return true;
            }
            return method.Name switch {
                nameof(PitTimer.ProgramChannel) => IsType(method.ContainingType, typeof(PitTimer)),
                nameof(Opl3Fm.WriteRegister) => IsType(method.ContainingType, typeof(Opl3Fm)),
                _ => false
            };
        }

        private bool HasAttribute(ISymbol symbol, Type type) =>
            symbol.GetAttributes().Any(attribute => IsType(attribute.AttributeClass, type));

        private bool IsServiceImplementation(ITypeSymbol receiver, IMethodSymbol method) {
            for (INamedTypeSymbol? type = receiver as INamedTypeSymbol; type is not null; type = type.BaseType) {
                foreach (IMethodSymbol declaration in type.GetMembers().OfType<IMethodSymbol>()) {
                    if (declaration.GetAttributes().Any(attribute =>
                        IsType(attribute.AttributeClass, typeof(InterruptServiceAttribute))
                        && attribute.NamedArguments.Any(argument =>
                            argument.Key == nameof(InterruptServiceAttribute.Implementation)
                            && argument.Value.Value is string name && name == method.Name))) {
                        return true;
                    }
                }
            }
            return false;
        }

        private static void Increment(Dictionary<string, int> counts, string key) =>
            counts[key] = counts.GetValueOrDefault(key) + 1;

        public override void VisitAssignmentExpression(AssignmentExpressionSyntax node) {
            if (node.Left is IdentifierNameSyntax flag && StatusFlagNames.Contains(flag.Identifier.ValueText)) {
                FlagAssignments++;
            }
            if (CompoundAssignmentKinds.Contains(node.Kind())) {
                CompoundAssignments++;
            }
            if (IsTupleSwap(node)) {
                TupleSwaps++;
            }
            if (_models[node.SyntaxTree].GetOperation(node) is ISimpleAssignmentOperation assignment
                && assignment.Target is IPropertyReferenceOperation property && IsRegister(property.Property)) {
                PlainRegisterAssignments++;
                IOperation value = assignment.Value;
                while (value is IConversionOperation conversion) {
                    value = conversion.Operand;
                }
                if (value is ILocalReferenceOperation) {
                    WriteBacks++;
                }
            }
            base.VisitAssignmentExpression(node);
        }

        private bool IsTupleSwap(AssignmentExpressionSyntax node) {
            if (node.Left is not TupleExpressionSyntax { Arguments.Count: 2 } left
                || node.Right is not TupleExpressionSyntax { Arguments.Count: 2 } right) {
                return false;
            }
            SemanticModel model = _models[node.SyntaxTree];
            ISymbol? first = model.GetSymbolInfo(left.Arguments[0].Expression).Symbol;
            ISymbol? second = model.GetSymbolInfo(left.Arguments[1].Expression).Symbol;
            return first is not null && second is not null
                && SymbolEqualityComparer.Default.Equals(first, model.GetSymbolInfo(right.Arguments[1].Expression).Symbol)
                && SymbolEqualityComparer.Default.Equals(second, model.GetSymbolInfo(right.Arguments[0].Expression).Symbol);
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
