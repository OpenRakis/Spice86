namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Passes;

using System.Linq;
using System.Numerics;

/// <summary>
/// Rewrites an instruction whose first statement is one ALU call (<c>add</c>, <c>sub</c>, <c>and</c>, <c>or</c>,
/// <c>xor</c>, <c>inc</c>, <c>dec</c>, <c>shl</c>, <c>shr</c>, <c>sar</c>, <c>rol</c>, <c>ror</c>, <c>rcl</c>,
/// <c>rcr</c>, <c>adc</c>, <c>sbb</c>, <c>mul</c>, <c>imul</c>, and <c>cmp</c> / <c>test</c> whose value is unused) as
/// plain code: an assignment, an assignment through a type conversion, or the initializer of a declaration becomes
/// plain arithmetic when at most one flag the call writes is live after it and that flag has a readable form; a
/// shift by a count that is not a constant becomes the masked shift when no flag it may write is live after it; an
/// unused call disappears when no flag it writes is live after it and its operands read no memory and call nothing.
/// The statements after the call keep their place and order; the flags live right after the call are computed
/// backward through them.
///
/// <para>The expansion is simplified on a scratch list with the general temporary rules
/// (<see cref="IrPlainTemporaryPropagation.Simplify"/>, then <see cref="IrConstantFolder.Fold"/>): the operand and
/// result declarations, the assignments of the live flags and the destination write (<c>dest = result</c>, after
/// the flag assignments when none of them reads the result, before them when one does). The rewrite commits only
/// when nothing of an expansion temp is left and the result is the destination write alone, one flag assignment
/// then the destination write, or - when the destination is a register of the result's type - the destination
/// write then one flag assignment that reads only that register and constants.</para>
/// </summary>
internal static class AluAssignmentRewrite {
    private const int MaxLiveFlags = 1;

    /// <summary>
    /// The replacement statements for an ALU-call statement in one of the four handled forms, or
    /// <c>null</c>. The IR passes supply <paramref name="liveAfterCall"/> from their SSA and
    /// <paramref name="origin"/> is the instruction the statement was lifted from, which the scratch
    /// expansion statements keep so the cleanup sees one instruction.
    /// </summary>
    internal static IReadOnlyList<IVisitableAstNode>? TryRewriteStatement(IVisitableAstNode statement,
        ICfgNode origin, StatusFlags liveAfterCall) => statement switch {
        AluCallValueNode unusedCall => TryRemoveUnusedCall(unusedCall, origin, liveAfterCall),
        BinaryOperationNode { BinaryOperation: BinaryOperation.ASSIGN, Right: AluCallValueNode call }
            assignment => TryRewriteAssignment(assignment, call, origin, liveAfterCall),
        BinaryOperationNode { BinaryOperation: BinaryOperation.ASSIGN,
                Right: TypeConversionNode { Value: AluCallValueNode call } conversion }
            assignment => TryRewriteWrapped(call, origin, liveAfterCall, Wrap(assignment, conversion),
                Wrap(assignment, conversion)),
        VariableDeclarationNode { Initializer: AluCallValueNode call } declaration =>
            TryRewriteWrapped(call, origin, liveAfterCall,
                value => declaration with { Initializer = value },
                value => new BinaryOperationNode(declaration.DataType, declaration.Reference,
                    BinaryOperation.ASSIGN, value)),
        _ => null
    };

    private static Func<ValueNode, IVisitableAstNode> Wrap(BinaryOperationNode assignment,
        TypeConversionNode conversion) =>
        value => assignment with { Right = conversion with { Value = value } };

    private static IReadOnlyList<IVisitableAstNode>? TryRewriteAssignment(BinaryOperationNode assignment,
        AluCallValueNode call, ICfgNode origin, StatusFlags liveAfterCall) {
        AluExpansion? expansion = AluExpansionBuilder.TryExpand(call);
        if (expansion is null) {
            return TryRewriteShiftValue(assignment, call, liveAfterCall);
        }
        return RewriteExpanded(expansion, call, origin, liveAfterCall,
            value => assignment with { Right = value });
    }

    /// <summary>
    /// The cleaned call wrapped by a type conversion or a declaration. For a declaration the destination write is
    /// built as an assignment to the temp's reference, so the scratch cleanup cannot read it as an unread
    /// declaration; the accepted shape maps it back to the declaration.
    /// </summary>
    private static IReadOnlyList<IVisitableAstNode>? TryRewriteWrapped(AluCallValueNode call,
        ICfgNode origin, StatusFlags liveAfterCall, Func<ValueNode, IVisitableAstNode> wrap,
        Func<ValueNode, IVisitableAstNode> destinationWrite) {
        AluExpansion? expansion = AluExpansionBuilder.TryExpand(call);
        if (expansion is null) {
            return null;
        }
        IReadOnlyList<IVisitableAstNode>? rewritten = RewriteExpanded(expansion, call, origin,
            liveAfterCall, destinationWrite);
        return rewritten?.Select(node => {
            if (node is BinaryOperationNode { BinaryOperation: BinaryOperation.ASSIGN, Right: ValueNode right,
                    Left: VariableReferenceNode }) {
                return wrap(right);
            }
            return node;
        }).ToList();
    }

    private static IReadOnlyList<IVisitableAstNode>? TryRemoveUnusedCall(AluCallValueNode call,
        ICfgNode origin, StatusFlags liveAfterCall) {
        AluExpansion? expansion = AluExpansionBuilder.TryExpand(call);
        if (expansion is null) {
            return null;
        }
        return RewriteExpanded(expansion, call, origin, liveAfterCall, null);
    }

    /// <summary>
    /// Builds the scratch list of one expansion and the destination write, simplifies it with the general rules
    /// and accepts only the shapes the cleaned expansion produced: no declaration of an expansion temp survives
    /// and the list is the destination write alone, one flag assignment then the destination write, or the
    /// destination write then one flag assignment that reads only the destination register and constants.
    /// The destination write reads the result temp; when a kept flag reads the result too and the destination is
    /// a register of the result's type, the write comes first so the copy-reuse rule turns the flag's reads into
    /// reads of that register.
    /// </summary>
    private static IReadOnlyList<IVisitableAstNode>? RewriteExpanded(AluExpansion expansion,
        AluCallValueNode call, ICfgNode origin, StatusFlags liveAfterCall,
        Func<ValueNode, IVisitableAstNode>? destinationWrite) {
        StatusFlags written = expansion.FlagAssignments.Aggregate(StatusFlags.None,
            (all, flag) => all | flag.Flag);
        StatusFlags liveWritten = written & liveAfterCall;
        if (BitOperations.PopCount((uint)liveWritten) > MaxLiveFlags
            || (liveWritten & ~ReadableFlags(call.Operation)) != StatusFlags.None) {
            return null;
        }
        List<BinaryOperationNode> flags = expansion.FlagAssignments
            .Where(assignment => (assignment.Flag & liveAfterCall) != StatusFlags.None)
            .Select(assignment => assignment.ToAssignment())
            .ToList();
        List<IVisitableAstNode> nodes = [.. expansion.Operands, expansion.Result];
        IVisitableAstNode? write = destinationWrite?.Invoke(expansion.Result.Reference);
        if (write is not null && flags.Any(flag => ReadsVariable(flag.Right, expansion.Result.VariableName))) {
            if (write is not BinaryOperationNode {
                    BinaryOperation: BinaryOperation.ASSIGN, Left: RegisterNode destination }
                || destination.DataType != expansion.Result.DataType) {
                return null;
            }
            nodes.Add(write);
            nodes.AddRange(flags);
        } else {
            nodes.AddRange(flags);
            if (write is not null) {
                nodes.Add(write);
            }
        }
        List<IVisitableAstNode> simplified = IrPlainTemporaryPropagation
            .Simplify(nodes.Select(node => new IrStatement(node, origin,
                IrEffectsVisitor.Of(node, false))).ToList())
            .Select(statement => IrConstantFolder.Fold(statement.Node,
                IrConstantFolder.NoVariableConstants))
            .ToList();
        return Accepted(simplified, expansion, write is not null) ? simplified : null;
    }

    private static bool Accepted(List<IVisitableAstNode> statements, AluExpansion expansion,
        bool hasDestination) {
        HashSet<string> temps = expansion.Operands.Select(operand => operand.VariableName)
            .Append(expansion.Result.VariableName).ToHashSet();
        if (statements.Any(statement => statement is VariableDeclarationNode declaration
                && temps.Contains(declaration.VariableName))) {
            return false;
        }
        if (!hasDestination) {
            return statements.Count == 0;
        }
        return statements switch {
            [BinaryOperationNode { BinaryOperation: BinaryOperation.ASSIGN, Left: not CpuFlagNode }] => true,
            [BinaryOperationNode { BinaryOperation: BinaryOperation.ASSIGN, Left: CpuFlagNode } flag,
                BinaryOperationNode { BinaryOperation: BinaryOperation.ASSIGN, Left: not CpuFlagNode }] =>
                ReadsNoFlag(flag.Right),
            [BinaryOperationNode { BinaryOperation: BinaryOperation.ASSIGN, Left: RegisterNode destination },
                BinaryOperationNode { BinaryOperation: BinaryOperation.ASSIGN, Left: CpuFlagNode } flag] =>
                destination.DataType == expansion.Result.DataType && ReadsOnly(flag.Right, destination),
            _ => false
        };
    }

    /// <summary>A kept flag assignment may not read a flag: the flag it assigns is written after the read, but
    /// the expansion's only flag-reading temp is the carry input, whose use it is not legal to fuse into the
    /// write.</summary>
    private static bool ReadsNoFlag(ValueNode node) =>
        !IrAstChildren.Descendants(node).Any(descendant => descendant is CpuFlagNode);

    private static bool ReadsVariable(IVisitableAstNode node, string name) =>
        IrAstChildren.Descendants(node).Any(descendant => descendant is VariableReferenceNode reference
            && reference.VariableName == name);

    private static bool ReadsOnly(ValueNode node, RegisterNode destination) => node switch {
        VariableReferenceNode => false,
        RegisterNode register => register == destination,
        ConstantNode => true,
        TypeConversionNode or UnaryOperationNode or BinaryOperationNode =>
            IrAstChildren.ChildrenOf(node).OfType<ValueNode>().All(child => ReadsOnly(child, destination)),
        _ => false
    };

    private static IReadOnlyList<IVisitableAstNode>? TryRewriteShiftValue(BinaryOperationNode assignment,
        AluCallValueNode call, StatusFlags liveAfterCall) {
        ValueNode? value = AluExpansionBuilder.TryExpandShiftValue(call);
        if (value is null
            || !AluFlagTable.TryGetEffect(call.Operation, call.Width, call.CallNode.Arguments,
                out FlagEffect effect)
            || (effect.MayWrite & liveAfterCall) != StatusFlags.None) {
            return null;
        }
        return [assignment with { Right = value }];
    }

    private static StatusFlags ReadableFlags(AluOperation operation) => operation switch {
        AluOperation.Add => StatusFlags.Zero | StatusFlags.Sign,
        AluOperation.Sub => StatusFlags.Carry | StatusFlags.Zero | StatusFlags.Sign,
        AluOperation.And or AluOperation.Or or AluOperation.Xor => StatusFlags.Carry | StatusFlags.Auxiliary
            | StatusFlags.Zero | StatusFlags.Sign | StatusFlags.Overflow,
        AluOperation.Inc or AluOperation.Dec => StatusFlags.Zero | StatusFlags.Sign | StatusFlags.Overflow,
        AluOperation.Shl or AluOperation.Shr or AluOperation.Sar => StatusFlags.Carry | StatusFlags.Zero | StatusFlags.Sign,
        AluOperation.Rol or AluOperation.Ror => StatusFlags.Carry,
        _ => StatusFlags.None
    };
}
