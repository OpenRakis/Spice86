namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Passes;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Propagation;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Relations;

using System.Linq;

/// <summary>
/// A conditional branch whose condition is a
/// <c>BuildSetCondition</c> flag shape gets the relation of the ALU setter that defines every flag
/// version it reads; a carry reader (<c>adc</c>, <c>sbb</c>, <c>rcl</c>, <c>rcr</c>) gets the setter's
/// carry relation substituted into its plain rewrite. Legality is stated with the SSA versions: an
/// operand of the relation is available when every register and token it reads has the same version
/// before the reader as before the setter; a memory operand that is not available is read once at the
/// setter into a <c>flagOperand_&lt;suffix&gt;</c> method-scope temporary. A setter whose flag versions
/// have no live use left but rewritten readers is dropped, keeping only its temporary assignment when
/// it has one.
/// </summary>
internal static class IrFlagPropagation {
    /// <summary>Rewrites the flag conditions and carry readers of <paramref name="method"/>.</summary>
    public static void Apply(IrMethod method, IrSsa ssa) {
        Rewrite rewrite = new(method, ssa);
        foreach (IrBlock block in method.Blocks) {
            rewrite.TryJump(block);
        }
        foreach (IrBlock block in method.Blocks) {
            for (int index = 0; index < block.Statements.Count; index++) {
                rewrite.TryCarryReader(block, index);
            }
        }
        rewrite.Finish();
    }

    private sealed class Rewrite {
        private readonly IrMethod _method;
        private readonly IrSsa _ssa;
        private readonly Dictionary<ICfgNode, List<IrOrigins.Located>> _byOrigin;
        private readonly HashSet<IrBlock> _entryBlocks;
        private readonly HashSet<ICfgNode> _entryNodes;
        private readonly List<(IrBlock Block, int Index, IReadOnlyList<IrStatement>? Nodes)> _edits = [];
        private readonly List<(IrBlock Block, IrBranch Branch)> _branchEdits = [];
        private readonly HashSet<object> _consumers = [];
        private readonly HashSet<IrStatement> _rewritten = [];
        private readonly Dictionary<IrStatement, Candidate> _candidates = [];
        private readonly Dictionary<IrStatement, MemoryOperandTemp> _tempsBySetter = [];
        private readonly HashSet<IrStatement> _tempRegistered = [];

        public Rewrite(IrMethod method, IrSsa ssa) {
            _method = method;
            _ssa = ssa;
            _byOrigin = IrOrigins.ByOrigin(method);
            _entryBlocks = method.Entries.Select(entry => entry.Block).ToHashSet();
            _entryNodes = method.Entries.Select(entry => entry.Node).ToHashSet();
        }

        /// <summary>The branch gets the setter's relation when the single-setter and legality rules hold.</summary>
        public void TryJump(IrBlock block) {
            if (block.Terminator is not IrBranch branch || _entryNodes.Contains(branch.Origin)
                || _method.Shapes.GetValueOrDefault(branch.Origin) == IrInstructionShape.RepCompare) {
                return;
            }
            if (FlagRelationRules.ConditionCodeOf(branch.Condition) is not { } code) {
                return;
            }
            IrValues flagReads = IrEffectsVisitor.Of(branch.Condition, false).Reads & IrValues.StatusFlags;
            if (flagReads == IrValues.None
                || SetterSite(flagReads, block, block.Statements.Count) is not { } site
                || !LegalSite(site)) {
                return;
            }
            if (MatchSetter(site.Statement.Node) is not { } shape
                || FlagSetter.Of(shape.Alu) is not { } setter) {
                return;
            }
            (IrBlock, int) readerPosition = (block, block.Statements.Count);
            FlagRelationOperands operands = OperandsOf(shape, site, readerPosition,
                shape.Destination is null && OnlyConsumers(site.Statement, block.Terminator));
            ValueNode? relation = FlagRelationRules.TryRelation(setter, code, operands);
            MemoryOperandTemp? temp = null;
            if (relation is null
                && TempOf(site, shape, operands) is { } found
                && FlagRelationRules.TryRelation(setter, code, found.Operands) is { } withTemp
                && ContainsReference(withTemp, found.Reference)) {
                relation = withTemp;
                temp = found;
            }
            if (relation is null) {
                return;
            }
            _branchEdits.Add((block, new IrBranch(branch.Origin, relation, branch.WhenTrue,
                branch.WhenFalse)));
            _consumers.Add(block.Terminator);
            if (temp is not null) {
                RegisterTemp(site, shape, temp);
            } else if (shape.Destination is null) {
                _candidates.TryAdd(site.Statement, new Candidate(site, shape, null, null));
            }
        }

        /// <summary>An <c>adc</c> / <c>sbb</c> / <c>rcl</c> / <c>rcr</c> reading the carry a single ALU
        /// setter defines gets the setter's carry relation substituted into its plain rewrite.</summary>
        public void TryCarryReader(IrBlock block, int index) {
            IrStatement statement = block.Statements[index];
            if (!IsCarryReader(statement.Node) || !Rewritable(statement.Origin)) {
                return;
            }
            IVisitableAstNode untyped = IrAluLowering.Untype(statement.Node);
            IrSsaVersionInfo info = _ssa.Info(_ssa.VersionBefore(block, index, IrSsaValue.Carry));
            if (info.Kind != IrSsaDefinitionKind.Definition
                || info.StatementIndex < 0 || info.StatementIndex >= info.Block.Statements.Count) {
                return;
            }
            (IrBlock Block, int Index, IrStatement Statement) site =
                (info.Block, info.StatementIndex, info.Block.Statements[info.StatementIndex]);
            if (!LegalSite(site)) {
                return;
            }
            if (MatchSetter(site.Statement.Node) is not { } shape
                || FlagSetter.Of(shape.Alu) is not { } setter) {
                return;
            }
            FlagRelationOperands operands = OperandsOf(shape, site, (block, index),
                shape.Destination is null && OnlyConsumers(site.Statement, statement));
            ValueNode? relation = FlagRelationRules.TryRelation(setter, 2, operands);
            MemoryOperandTemp? temp = null;
            if (relation is null
                && TempOf(site, shape, operands) is { } found
                && FlagRelationRules.TryRelation(setter, 2, found.Operands) is { } withTemp
                && ContainsReference(withTemp, found.Reference)) {
                relation = withTemp;
                temp = found;
            }
            if (relation is null
                || AluAssignmentRewrite.TryRewriteStatement(untyped,
                    IrFlagLiveness.LiveAfter(_ssa, block, index)) is not { } plain
                || FlagReadSubstitution.SubstituteCarry(new BlockNode(plain.ToArray()), relation)
                    is not { } substituted) {
                return;
            }
            _edits.Add((block, index,
                substituted.Statements.Select(node => IrStatement.Create(node, statement.Origin)).ToList()));
            _rewritten.Add(statement);
            _consumers.Add(statement);
            if (temp is not null) {
                RegisterTemp(site, shape, temp);
            } else if (shape.Destination is null) {
                _candidates.TryAdd(site.Statement, new Candidate(site, shape, null, null));
            }
        }

        /// <summary>Drops the setters whose flags only feed rewritten readers, then applies the edits
        /// bottom-up so earlier indices stay valid, then replaces the branch terminators.</summary>
        public void Finish() {
            foreach ((IrStatement statement, Candidate candidate) in _candidates) {
                if (_rewritten.Contains(statement)) {
                    continue;
                }
                bool onlyConsumers = OnlyConsumers(statement);
                if (candidate.TempAssign is null) {
                    if (onlyConsumers) {
                        _edits.Add((candidate.Site.Block, candidate.Site.Index, null));
                    }
                    continue;
                }
                ICfgNode origin = candidate.Site.Statement.Origin;
                _edits.Add((candidate.Site.Block, candidate.Site.Index,
                    onlyConsumers
                        ? new[] { IrStatement.Create(candidate.TempAssign, origin) }
                        : new[] { IrStatement.Create(candidate.TempAssign, origin),
                            IrStatement.Create(candidate.TempCall!, origin) }));
            }
            foreach ((IrBlock block, int index, IReadOnlyList<IrStatement>? nodes)
                in _edits.OrderByDescending(edit => edit.Index)) {
                IrStatement old = block.Statements[index];
                block.Statements.RemoveAt(index);
                if (nodes is null) {
                    _ssa.Remove(old);
                    continue;
                }
                for (int i = nodes.Count - 1; i >= 0; i--) {
                    block.Statements.Insert(index, nodes[i]);
                }
                _ssa.Replace(old, nodes);
            }
            foreach ((IrBlock block, IrBranch branch) in _branchEdits) {
                block.Terminator = branch;
            }
        }

        /// <summary>The statement every flag version of <paramref name="flagReads"/> used at
        /// (<paramref name="block"/>, <paramref name="index"/>) is defined by, or <c>null</c> when the
        /// versions come from different sites or from phis.</summary>
        private (IrBlock Block, int Index, IrStatement Statement)? SetterSite(IrValues flagReads,
            IrBlock block, int index) {
            (IrBlock, int, IrStatement)? site = null;
            foreach ((StatusFlags _, IrSsaValue value) in IrFlagLiveness.StatusFlagValues) {
                if ((flagReads & IrSsaValues.MaskOf(value)) == IrValues.None) {
                    continue;
                }
                IrSsaVersionInfo info = _ssa.Info(_ssa.VersionBefore(block, index, value));
                if (info.Kind != IrSsaDefinitionKind.Definition || info.StatementIndex < 0
                    || info.StatementIndex >= info.Block.Statements.Count) {
                    return null;
                }
                if (site is { } found
                    && (found.Item1 != info.Block || found.Item2 != info.StatementIndex)) {
                    return null;
                }
                site = (info.Block, info.StatementIndex, info.Block.Statements[info.StatementIndex]);
            }
            return site;
        }

        /// <summary>A setter site that cannot fault, has no after-check and is not a method entry block
        /// reached in the middle of the method.</summary>
        private bool LegalSite((IrBlock Block, int Index, IrStatement Statement) site) =>
            !ReachedInTheMiddle(site.Block) && Rewritable(site.Statement.Origin);

        private bool ReachedInTheMiddle(IrBlock block) =>
            _entryBlocks.Contains(block) && _method.Blocks.Any(other => other.Terminator.Edges
                .Any(edge => ReferenceEquals(edge.Target, block)));

        private bool Rewritable(ICfgNode origin) =>
            _byOrigin.TryGetValue(origin, out List<IrOrigins.Located>? statements)
            && IrOrigins.Rewritable(statements, origin);

        /// <summary>The relation operands of <paramref name="shape"/>: an operand is available when
        /// every register and token it reads has the same version before the reader as before the
        /// setter; a memory operand is available only when the setter can be dropped and nothing
        /// between them can touch memory or machine state; the result register is available when the
        /// version the setter defines still holds at the reader.</summary>
        private FlagRelationOperands OperandsOf(SetterShape shape,
            (IrBlock Block, int Index, IrStatement Statement) site, (IrBlock Block, int Index) reader,
            bool droppable) {
            IReadOnlyList<IVisitableAstNode> arguments = shape.Alu.Call.CallNode.Arguments;
            ValueNode? left = arguments.Count > 0 ? arguments[0] as ValueNode : null;
            ValueNode? right = arguments.Count > 1 ? arguments[1] as ValueNode : null;
            (IrBlock, int) setter = (site.Block, site.Index);
            return new FlagRelationOperands(Available(left, site, reader, droppable),
                Available(right, site, reader, droppable), ResultOf(shape.Destination, setter, reader));
        }

        private ValueNode? Available(ValueNode? operand,
            (IrBlock Block, int Index, IrStatement Statement) site, (IrBlock Block, int Index) reader,
            bool droppable) {
            if (operand is null || Classify(operand) == OperandKind.Other) {
                return null;
            }
            IrEffects effects = IrEffectsVisitor.Of(operand, false);
            (IrBlock, int) setter = (site.Block, site.Index);
            if (!_ssa.SameVersions(effects.Reads, effects.TokenReads, setter, reader)) {
                return null;
            }
            if (Classify(operand) == OperandKind.Memory
                && !MemoryOperandAvailable(site, reader, droppable)) {
                return null;
            }
            return operand;
        }

        /// <summary>A memory operand can be re-read at the reader only when the setter is dropped (its
        /// own read is gone) and every statement between them stays inside the same block without an
        /// entry node, a boundary node or a statement whose effects touch memory. An event check needs
        /// no refusal of its own: its may-write re-versions the registers a handler can observe.</summary>
        private bool MemoryOperandAvailable(
            (IrBlock Block, int Index, IrStatement Statement) site, (IrBlock Block, int Index) reader,
            bool droppable) {
            if (!droppable || site.Block != reader.Block) {
                return false;
            }
            for (int index = site.Index + 1; index < reader.Index; index++) {
                IrStatement between = site.Block.Statements[index];
                if (_entryNodes.Contains(between.Origin)) {
                    return false;
                }
                if (between.Node is InstructionStartNode or CommentNode) {
                    continue;
                }
                if (between.Node is SpeculativeGuardNode || between.Effects.TouchesMemory) {
                    return false;
                }
            }
            return true;
        }

        private ValueNode? ResultOf(RegisterNode? destination, (IrBlock Block, int Index) setter,
            (IrBlock Block, int Index) reader) {
            if (destination is null) {
                return null;
            }
            uint bits = IrSsaValues.ValueBits(IrValueSets.Of(destination));
            for (int v = 0; v < (int)IrSsaValue.Count; v++) {
                if ((bits & (1u << v)) != 0
                    && _ssa.VersionAfter(setter.Block, setter.Index, (IrSsaValue)v)
                        != _ssa.VersionBefore(reader.Block, reader.Index, (IrSsaValue)v)) {
                    return null;
                }
            }
            return destination;
        }

        /// <summary>The temporary of a setter whose exactly one relation operand is memory and
        /// unavailable, so the operand is read once at the setter into
        /// <c>flagOperand_&lt;suffix&gt;</c> and the relation reads it there.</summary>
        private MemoryOperandTemp? TempOf((IrBlock Block, int Index, IrStatement Statement) site,
            SetterShape shape, FlagRelationOperands operands) {
            if (_tempsBySetter.TryGetValue(site.Statement, out MemoryOperandTemp? existing)) {
                return existing;
            }
            IReadOnlyList<IVisitableAstNode> arguments = shape.Alu.Call.CallNode.Arguments;
            ValueNode? left = arguments.Count > 0 ? arguments[0] as ValueNode : null;
            ValueNode? right = arguments.Count > 1 ? arguments[1] as ValueNode : null;
            bool leftBlocked = left is not null && operands.Left is null && Classify(left) == OperandKind.Memory;
            bool rightBlocked = right is not null && operands.Right is null && Classify(right) == OperandKind.Memory;
            if (leftBlocked == rightBlocked) {
                return null;
            }
            int argumentIndex = leftBlocked ? 0 : 1;
            if ((argumentIndex == 0 ? left : right) is not { } operand) {
                return null;
            }
            MethodScopedVariableReferenceNode reference = new(operand.DataType,
                $"flagOperand_{SuffixOf(site.Statement.Origin)}");
            MemoryOperandTemp temp = new(reference, operand, argumentIndex, new FlagRelationOperands(
                argumentIndex == 0 ? reference : operands.Left,
                argumentIndex == 1 ? reference : operands.Right,
                operands.Result));
            _tempsBySetter[site.Statement] = temp;
            return temp;
        }

        /// <summary>Declares the method-scope temporary once and rewires the setter statement to read
        /// the operand into it: dropped form for a destination-less setter (candidate), stay form
        /// otherwise (the assign and the re-operanded call replace the statement).</summary>
        private void RegisterTemp((IrBlock Block, int Index, IrStatement Statement) site,
            SetterShape shape, MemoryOperandTemp temp) {
            if (!_tempRegistered.Add(site.Statement)) {
                return;
            }
            if (_method.MethodVariables.All(variable => variable.VariableName != temp.Reference.VariableName)) {
                _method.MethodVariables.Add(new VariableDeclarationNode(temp.Operand.DataType,
                    temp.Reference.VariableName, new ConstantNode(temp.Operand.DataType, 0)));
            }
            BinaryOperationNode assign = new(temp.Operand.DataType, temp.Reference,
                BinaryOperation.ASSIGN, temp.Operand);
            IVisitableAstNode callStatement = CallWithOperand(shape, temp);
            if (shape.Destination is null) {
                _candidates[site.Statement] = new Candidate(site, shape, assign, callStatement);
            } else {
                _edits.Add((site.Block, site.Index,
                    new[] { IrStatement.Create(assign, site.Statement.Origin),
                        IrStatement.Create(callStatement, site.Statement.Origin) }));
                _rewritten.Add(site.Statement);
            }
        }

        /// <summary>The setter's node with argument <paramref name="temp"/>'s index replaced by the
        /// temporary reference, the typed wrappers kept.</summary>
        private static IVisitableAstNode CallWithOperand(SetterShape shape, MemoryOperandTemp temp) {
            IVisitableAstNode[] arguments = shape.Alu.Call.CallNode.Arguments
                .Select((argument, index) => index == temp.ArgumentIndex ? temp.Reference : argument)
                .ToArray();
            MethodCallValueNode call = shape.Alu.Call.WithArguments(arguments);
            return shape.Node switch {
                AluOperationNode alu => alu with { Call = call },
                BinaryOperationNode { Right: AluOperationNode alu } assignment =>
                    assignment with { Right = alu with { Call = call } },
                BinaryOperationNode { Right: TypeConversionNode { Value: AluOperationNode alu } conversion }
                    assignment => assignment with { Right = conversion with { Value = alu with { Call = call } } },
                VariableDeclarationNode { Initializer: AluOperationNode alu } declaration =>
                    declaration with { Initializer = alu with { Call = call } },
                _ => shape.Node
            };
        }

        /// <summary>Every live use of every flag version <paramref name="statement"/> defines is one of
        /// the rewritten readers or <paramref name="extra"/>.</summary>
        private bool OnlyConsumers(IrStatement statement) => OnlyConsumers(statement, null);

        private bool OnlyConsumers(IrStatement statement, object? extra) {
            foreach ((IrSsaValue value, int version) in _ssa.Definitions(statement)) {
                if (!IrFlagLiveness.IsStatusFlagValue(value)) {
                    continue;
                }
                foreach (object? site in _ssa.LiveUses(version)
                        .Select(use => use.Terminator ?? (object?)use.Statement)) {
                    if (site is null || (!ReferenceEquals(site, extra) && !_consumers.Contains(site))) {
                        return false;
                    }
                }
            }
            return true;
        }

        private string SuffixOf(ICfgNode origin) =>
            _method.LocalSuffixes.TryGetValue(origin, out string? suffix)
                ? suffix
                : origin.Address.Offset.ToString("X4");
    }

    /// <summary>Whether <paramref name="statement"/> is a carry reader: an assignment (optionally through
    /// a conversion or a declaration) of an <c>adc</c> / <c>sbb</c> / <c>rcl</c> / <c>rcr</c>, or a bare
    /// call.</summary>
    private static bool IsCarryReader(IVisitableAstNode statement) {
        AluOperationNode? alu = statement switch {
            BinaryOperationNode { BinaryOperation: BinaryOperation.ASSIGN,
                    Right: AluOperationNode direct } => direct,
            BinaryOperationNode { BinaryOperation: BinaryOperation.ASSIGN,
                    Right: TypeConversionNode { Value: AluOperationNode converted } } => converted,
            VariableDeclarationNode { Initializer: AluOperationNode declared } => declared,
            AluOperationNode bare => bare,
            _ => null
        };
        return alu?.Operation is AluOperation.Adc or AluOperation.Sbb or AluOperation.Rcl
            or AluOperation.Rcr;
    }

    /// <summary>The ALU setter statement - an unused <see cref="AluOperationNode"/> call, a register
    /// assignment of one (optionally through a conversion), or a declaration over one - with its call
    /// and destination register.</summary>
    private static SetterShape? MatchSetter(IVisitableAstNode node) => node switch {
        AluOperationNode alu => new SetterShape(node, alu, null),
        BinaryOperationNode { BinaryOperation: BinaryOperation.ASSIGN,
                Left: RegisterNode destination and not SegmentRegisterNode,
                Right: AluOperationNode alu } =>
            new SetterShape(node, alu, destination),
        BinaryOperationNode { BinaryOperation: BinaryOperation.ASSIGN,
                Left: RegisterNode destination and not SegmentRegisterNode,
                Right: TypeConversionNode { Value: AluOperationNode alu } } =>
            new SetterShape(node, alu, destination),
        VariableDeclarationNode { Initializer: AluOperationNode alu } =>
            new SetterShape(node, alu, null),
        _ => null
    };

    /// <summary>Whether <paramref name="node"/> reads <paramref name="reference"/> anywhere; only the
    /// listed value shapes are descended.</summary>
    private static bool ContainsReference(ValueNode node, VariableReferenceNode reference) => node switch {
        VariableReferenceNode r => r == reference,
        TypeConversionNode or UnaryOperationNode or BinaryOperationNode or SegmentedPointerNode
            or AbsolutePointerNode or MethodCallValueNode =>
            IrAstChildren.ChildrenOf(node).OfType<ValueNode>()
                .Any(child => ContainsReference(child, reference)),
        _ => false
    };

    private static OperandKind Classify(IVisitableAstNode node) => node switch {
        RegisterNode or ConstantNode => OperandKind.Plain,
        TypeConversionNode conversion => Classify(conversion.Value),
        UnaryOperationNode unary => Classify(unary.Value),
        BinaryOperationNode { BinaryOperation: not BinaryOperation.ASSIGN } binary =>
            Worst(Classify(binary.Left), Classify(binary.Right)),
        InstructionFieldNode field => field.ResolvedNode is ConstantNode ? OperandKind.Plain : OperandKind.Memory,
        SegmentedPointerNode or AbsolutePointerNode => OperandKind.Memory,
        _ => OperandKind.Other
    };

    private static OperandKind Worst(OperandKind first, OperandKind second) =>
        first >= second ? first : second;

    private enum OperandKind {
        Plain,
        Memory,
        Other
    }

    private sealed record SetterShape(IVisitableAstNode Node, AluOperationNode Alu,
        RegisterNode? Destination);

    private sealed record Candidate((IrBlock Block, int Index, IrStatement Statement) Site,
        SetterShape Shape, BinaryOperationNode? TempAssign, IVisitableAstNode? TempCall);

    private sealed record MemoryOperandTemp(MethodScopedVariableReferenceNode Reference, ValueNode Operand,
        int ArgumentIndex, FlagRelationOperands Operands);
}
