namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Passes;
using Spice86.Shared.Emulator.Memory;

using System.Linq;

/// <summary>
/// Pair promotion: when two accepted non-single-block 16-bit scalar webs are the halves of one
/// wide value (every definition of one pairs with a definition of the other - a double-precision
/// triple, an adjacent low/high assignment pair, or a phi of the same block), they print as one
/// <see cref="DataType.UINT32"/> method-scope local whose slices serve the half reads and
/// write-backs. Pairing is decided from the definition sites the scalar analysis collected, never
/// from register names; any unpaired definition keeps both webs on their scalar plans.
/// </summary>
internal static partial class RegisterLocalPromotion {

    /// <summary>How one definition of the low web pairs with one of the high web.</summary>
    private sealed record PairAction(Definition Low, Definition High);

    /// <summary>Attempts every unordered pair of promoted unsigned word webs and promotes the
    /// completely validated ones to a shared 32-bit local. Runs after the scalar plans are built
    /// and rewrites them in place: scalar declarations and read/write-back maps of a paired web are
    /// replaced by the pair's forms. Falls back to the scalar plan whenever a definition of either
    /// web has no partner.</summary>
    private static void PlanPairs(IrMethod method, List<(Candidate Candidate, string Name)> accepted,
        IReadOnlySet<string> reservedNames, HashSet<string> declared,
        Dictionary<IVisitableAstNode, SubstitutedRead> reads,
        List<LocalDeclarationNode> declarations, List<RegisterLocalWriteBack> writeBacks,
        HashSet<IVisitableAstNode> dropped,
        Dictionary<IVisitableAstNode, IVisitableAstNode> replacements) {
        List<(Candidate Candidate, string Name)> eligible = [.. accepted.Where(item =>
            !item.Candidate.SingleBlock && item.Candidate.Register.DataType == DataType.UINT16
            && item.Candidate.Register is not SegmentRegisterNode)];
        HashSet<Candidate> consumed = new(ReferenceEqualityComparer.Instance);
        HashSet<ICfgNode> pinnedOrigins = IrInstructionIdioms.PinnedOriginsOf(method);
        int counter = 0;
        foreach ((Candidate candidate, string name) in eligible) {
            if (consumed.Contains(candidate)) {
                continue;
            }
            foreach ((Candidate other, string otherName) in eligible) {
                if (ReferenceEquals(candidate, other) || consumed.Contains(other)
                    || (IrValueSets.Of(candidate.Register) & IrValueSets.Of(other.Register))
                        != CpuValues.None) {
                    continue;
                }
                if (PairDefinitions(method, candidate, other, pinnedOrigins)
                        is not { } pairing) {
                    continue;
                }
                string localName;
                do {
                    localName = $"value{++counter}";
                } while (reservedNames.Contains(localName) || declared.Contains(localName));
                declared.Add(localName);
                (Candidate low, string lowName, Candidate high, string highName) = pairing.LowFirst
                    ? (candidate, name, other, otherName) : (other, otherName, candidate, name);
                ApplyPair(method, low, lowName, high, highName, localName, pairing.Actions,
                    reads, declarations, writeBacks, dropped, replacements);
                consumed.Add(candidate);
                consumed.Add(other);
                break;
            }
        }
    }

    /// <summary>The pairing of two candidates: which one is the low half and one action per
    /// definition pair.</summary>
    private sealed record Pairing(bool LowFirst, List<PairAction> Actions);

    /// <summary>Matches every definition of <paramref name="first"/> with one of
    /// <paramref name="second"/> on the same block: two phis of one block, the two half slices of
    /// one wide temporary, or two adjacent plain word assignments. Returns the consistent
    /// orientation or <see langword="null"/> when a definition has no partner, when partners
    /// disagree on which web is low, or when no definition is a genuine double-precision triple -
    /// adjacent plain assignments can seed the web but cannot alone prove a wide value.</summary>
    private static Pairing? PairDefinitions(IrMethod method, Candidate first, Candidate second,
            HashSet<ICfgNode> pinnedOrigins) {
        List<PairAction> actions = [];
        HashSet<Definition> used = new(ReferenceEqualityComparer.Instance);
        int orientation = 0;
        bool statementPair = false;
        bool sawTriple = false;
        foreach (Definition definition in first.Sites) {
            Definition? partner = null;
            int role = 0;
            foreach (Definition other in second.Sites) {
                if (used.Contains(other)) {
                    continue;
                }
                if (MatchDefinitions(method, definition, other, pinnedOrigins)
                        is { } matched) {
                    partner = other;
                    role = matched;
                    break;
                }
            }
            if (partner is null) {
                return null;
            }
            used.Add(partner);
            if (role != 0) {
                statementPair = true;
                int direction = Math.Sign(role);
                if (orientation != 0 && orientation != direction) {
                    return null;
                }
                orientation = direction;
                sawTriple |= Math.Abs(role) == 1;
            }
            actions.Add(role >= 0
                ? new PairAction(definition, partner)
                : new PairAction(partner, definition));
        }
        if (used.Count != second.Sites.Count || !statementPair || !sawTriple) {
            return null;
        }
        return new Pairing(orientation >= 0, actions);
    }

    /// <summary>Whether <paramref name="aDef"/> and <paramref name="bDef"/> are one definition of
    /// a pair; the sign gives the orientation (positive when <paramref name="aDef"/> is the low
    /// half) and the magnitude the evidence kind: 1 for a double-precision triple, 2 for an
    /// adjacent assignment pair, 0 for a phi pair that carries no orientation.</summary>
    private static int? MatchDefinitions(IrMethod method, Definition aDef, Definition bDef,
            HashSet<ICfgNode> pinnedOrigins) {
        if (!ReferenceEquals(aDef.Block, bDef.Block)) {
            return null;
        }
        if (aDef.Statement is null || bDef.Statement is null) {
            return aDef.Statement is null && bDef.Statement is null ? 0 : null;
        }
        if (aDef.Index < bDef.Index && EffectivelyAdjacent(aDef, bDef)
            && !EntryBetween(aDef.Block, aDef.Index, bDef.Index, pinnedOrigins)) {
            if (TripleHalf(aDef, true) is { } lowDecl && TripleHalf(bDef, false) is { } highDecl
                && lowDecl == highDecl
                && TemporaryUses(method, lowDecl) == 2) {
                return 1;
            }
            return AdjacentPair(aDef, bDef) ? 2 : null;
        }
        if (bDef.Index < aDef.Index && EffectivelyAdjacent(bDef, aDef)
            && !EntryBetween(bDef.Block, bDef.Index, aDef.Index, pinnedOrigins)) {
            if (TripleHalf(bDef, true) is { } lowDecl2 && TripleHalf(aDef, false) is { } highDecl2
                && lowDecl2 == highDecl2
                && TemporaryUses(method, lowDecl2) == 2) {
                return -1;
            }
            return AdjacentPair(bDef, aDef) ? -2 : null;
        }
        return null;
    }

    /// <summary>Whether only markers sit between the two definitions: they are the first and
    /// second halves of one definition site.</summary>
    private static bool EffectivelyAdjacent(Definition first, Definition second) {
        List<IrStatement> statements = first.Block.Statements;
        for (int i = first.Index + 1; i < second.Index; i++) {
            if (!IrInstructionIdioms.IsMarker(statements[i])) {
                return false;
            }
        }
        return true;
    }

    /// <summary>Whether a pinned origin (a method entry or a structural-successor anchor) lands
    /// on a statement inside <paramref name="block"/> between <paramref name="from"/> (exclusive)
    /// and <paramref name="to"/> (inclusive): control could then reach the second half of a
    /// definition pair without the first.</summary>
    private static bool EntryBetween(IrBlock block, int from, int to,
            HashSet<ICfgNode> pinnedOrigins) =>
        IrInstructionIdioms.CrossesPinnedOrigin(block.Statements, from, to, pinnedOrigins);

    /// <summary>How many <see cref="VariableReferenceNode"/> instances named
    /// <paramref name="name"/> the method holds: a pair temporary is used exactly by its two
    /// half slices; any other read keeps it live.</summary>
    private static int TemporaryUses(IrMethod method, string name) => method.Blocks
        .SelectMany(block => block.Statements
            .SelectMany(statement => IrAstChildren.Descendants(statement.Node))
            .Concat(block.HasTerminator
                ? IrAstChildren.RootsOf(block.Terminator).SelectMany(IrAstChildren.Descendants)
                : []))
        .OfType<VariableReferenceNode>().Count(reference => reference.VariableName == name);

    /// <summary>The name of the wide temporary <paramref name="definition"/>'s statement slices:
    /// for a low half <c>half = (ushort)tmp</c>, for a high half <c>half = (ushort)(tmp &gt;&gt;
    /// 16)</c>. The statement sits inside a declaration-triple: the temporary's declaration is the
    /// previous effective statement of the low half, which itself precedes the high half.</summary>
    private static string? TripleHalf(Definition definition, bool low) {
        if (definition.Statement?.Node is not BinaryOperationNode {
                BinaryOperation: BinaryOperation.ASSIGN, Right: TypeConversionNode conversion }) {
            return null;
        }
        if (conversion.DataType != DataType.UINT16) {
            return null;
        }
        string? name;
        if (low) {
            name = conversion.Value is VariableReferenceNode { DataType: { } type } reference
                && type == DataType.UINT32 ? reference.VariableName : null;
        } else {
            name = conversion.Value is BinaryOperationNode {
                    BinaryOperation: BinaryOperation.RIGHT_SHIFT,
                    Left: VariableReferenceNode { DataType: { } type } reference,
                    Right: ConstantNode { Value: 16 } }
                && type == DataType.UINT32 ? reference.VariableName : null;
        }
        if (name is null) {
            return null;
        }
        List<IrStatement> statements = definition.Block.Statements;
        int index = low ? definition.Index - 1 : definition.Index - 2;
        if (index < 0) {
            return null;
        }
        return statements[index].Node is VariableDeclarationNode {
            DataType: { } declType, VariableName: { } declName } && declType == DataType.UINT32
            && declName == name ? name : null;
    }

    /// <summary>Whether <paramref name="first"/> and <paramref name="second"/> are adjacent plain
    /// word assignments (<paramref name="first"/> at the lower index): both right sides are pure
    /// word values, neither statement faults, interrupts or writes a token, and the second right
    /// side does not read the first target.</summary>
    private static bool AdjacentPair(Definition first, Definition second) =>
        first.Statement is { } firstStatement && second.Statement is { } secondStatement
        && PlainAssign(firstStatement) is not null
        && PlainAssign(secondStatement) is { } highRight
        && firstStatement.Node is BinaryOperationNode { Left: RegisterNode firstTarget }
        && DoesNotReadRegister(highRight, firstTarget);

    /// <summary>The pure word right side of a plain, side-effect-free register assignment, or
    /// null.</summary>
    private static ValueNode? PlainAssign(IrStatement statement) {
        if (statement.Node is not BinaryOperationNode {
                BinaryOperation: BinaryOperation.ASSIGN, Left: RegisterNode, Right: { } right }) {
            return null;
        }
        IrEffects effects = statement.Effects;
        return !effects.MayFault && !effects.DeliversInterrupts
            && effects.TokenWrites == IrTokens.None
            && right.DataType == DataType.UINT16 && IrVerifier.IsPureArm(right) ? right : null;
    }

    private static bool DoesNotReadRegister(ValueNode node, RegisterNode register) =>
        (IrEffectsVisitor.Of(node, false).Reads & IrValueSets.Of(register)) == CpuValues.None;

    /// <summary>Applies a validated pairing: the scalar declarations and read/write-back entries
    /// of both webs become the pair's, each triple or adjacent definition pair rewrites as an
    /// assignment of the wide local, whole-pair read expressions substitute it directly.</summary>
    private static void ApplyPair(IrMethod method, Candidate low, string lowName, Candidate high,
        string highName, string localName, List<PairAction> actions,
        Dictionary<IVisitableAstNode, SubstitutedRead> reads,
        List<LocalDeclarationNode> declarations, List<RegisterLocalWriteBack> writeBacks,
        HashSet<IVisitableAstNode> dropped,
        Dictionary<IVisitableAstNode, IVisitableAstNode> replacements) {
        declarations.RemoveAll(declaration =>
            declaration.VariableName == lowName || declaration.VariableName == highName);
        declarations.Add(new LocalDeclarationNode(DataType.UINT32, localName,
            new ConstantNode(DataType.UINT32, 0)));
        MethodScopedVariableReferenceNode Local() =>
            new(DataType.UINT32, localName);
        foreach (PairAction action in actions) {
            if (action.Low.Statement is not { } lowStatement
                || action.High.Statement is not { } highStatement) {
                continue;
            }
            if (TripleHalf(action.Low, true) is not null) {
                // The declaration keeps its origin and evaluation point; only its two half
                // slices go away.
                VariableDeclarationNode declaration =
                    (VariableDeclarationNode)action.Low.Block.Statements[action.Low.Index - 1].Node;
                replacements[declaration] = new BinaryOperationNode(DataType.UINT32, Local(),
                    BinaryOperation.ASSIGN, declaration.Initializer);
                dropped.Add(lowStatement.Node);
                dropped.Add(highStatement.Node);
                continue;
            }
            ValueNode lowRight = ((BinaryOperationNode)lowStatement.Node).Right;
            ValueNode highRight = ((BinaryOperationNode)highStatement.Node).Right;
            dropped.Add(lowStatement.Node);
            replacements[highStatement.Node] = new BinaryOperationNode(DataType.UINT32,
                Local(), BinaryOperation.ASSIGN,
                (ValueNode)IrConstantFolder.FoldConstantTerms(IrConstantFolder.Fold(
                    IrDoublePrecision.Pair(DataType.UINT32, lowRight, highRight, 16),
                    IrConstantFolder.NoVariableConstants)));
        }
        foreach (RegisterNode target in low.DefTargets.Concat(high.DefTargets)) {
            reads.Remove(target);
        }
        HashSet<IVisitableAstNode> lowReads =
            new(low.Reads.Select(read => read.Node), ReferenceEqualityComparer.Instance);
        HashSet<IVisitableAstNode> highReads =
            new(high.Reads.Select(read => read.Node), ReferenceEqualityComparer.Instance);
        foreach ((IVisitableAstNode node, IVisitableAstNode? _) in low.Reads) {
            reads[node] = new SubstitutedRead($"(ushort){localName}", null);
        }
        foreach ((IVisitableAstNode node, IVisitableAstNode? _) in high.Reads) {
            reads[node] = new SubstitutedRead($"(ushort)({localName} >> 16)", null);
        }
        foreach (IVisitableAstNode root in method.Blocks
                .SelectMany(block => block.Statements.Select(statement => (IVisitableAstNode)statement.Node)
                    .Concat(IrAstChildren.RootsOf(block.Terminator)))) {
            foreach (BinaryOperationNode or in IrAstChildren.Descendants(root)
                    .OfType<BinaryOperationNode>()) {
                if (or.DataType != DataType.UINT32
                    || or.BinaryOperation != BinaryOperation.BITWISE_OR
                    || IrDoublePrecision.ZeroExtendedHalf(or.Left, DataType.UINT16,
                            DataType.UINT32) is not { } loRead
                    || !lowReads.Contains(loRead)
                    || or.Right is not BinaryOperationNode {
                        BinaryOperation: BinaryOperation.LEFT_SHIFT,
                        Left: { } shiftLeft, Right: ConstantNode { Value: 16 } }
                    || IrDoublePrecision.ZeroExtendedHalf(shiftLeft, DataType.UINT16,
                            DataType.UINT32) is not { } hiRead
                    || !highReads.Contains(hiRead)) {
                    continue;
                }
                replacements[or] = Local();
            }
        }
        for (int i = 0; i < writeBacks.Count; i++) {
            if (writeBacks[i].Node is not RegisterWriteBackNode writeBack) {
                continue;
            }
            ValueNode? slice = null;
            if (writeBack.Local.VariableName == lowName) {
                slice = new TypeConversionNode(DataType.UINT16, Local());
            } else if (writeBack.Local.VariableName == highName) {
                slice = new TypeConversionNode(DataType.UINT16,
                    new BinaryOperationNode(DataType.UINT32, Local(),
                        BinaryOperation.RIGHT_SHIFT, new ConstantNode(DataType.INT32, 16)));
            }
            if (slice is null) {
                continue;
            }
            writeBacks[i] = new RegisterLocalWriteBack(writeBacks[i].Instruction,
                writeBacks[i].Anchor, new BinaryOperationNode(DataType.UINT16,
                    FreshRegister(writeBack.Register), BinaryOperation.ASSIGN, slice));
        }
    }
}
