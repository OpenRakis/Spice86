namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

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
/// </summary>
internal static class AluAssignmentRewrite {
    private const int MaxLiveFlags = 1;

    /// <summary>
    /// The replacement statements for an ALU-call statement in one of the four handled forms, or
    /// <c>null</c>. The IR passes supply <paramref name="liveAfterCall"/> from their SSA.
    /// </summary>
    internal static IReadOnlyList<IVisitableAstNode>? TryRewriteStatement(IVisitableAstNode statement,
        StatusFlags liveAfterCall) => statement switch {
        AluCallValueNode unusedCall => TryRemoveUnusedCall(unusedCall, liveAfterCall),
        BinaryOperationNode { BinaryOperation: BinaryOperation.ASSIGN, Right: AluCallValueNode call }
            assignment => TryRewriteAssignment(assignment, call, liveAfterCall),
        BinaryOperationNode { BinaryOperation: BinaryOperation.ASSIGN,
                Right: TypeConversionNode { Value: AluCallValueNode call } conversion }
            assignment => TryRewriteWrapped(call, liveAfterCall,
                value => assignment with { Right = conversion with { Value = value } }),
        VariableDeclarationNode { Initializer: AluCallValueNode call } declaration =>
            TryRewriteWrapped(call, liveAfterCall,
                value => declaration with { Initializer = value }),
        _ => null
    };

    private static IReadOnlyList<IVisitableAstNode>? TryRewriteAssignment(BinaryOperationNode assignment,
        AluCallValueNode call, StatusFlags liveAfterCall) {
        AluExpansion? expansion = AluExpansionBuilder.TryExpand(call);
        if (expansion is null) {
            return TryRewriteShiftValue(assignment, call, liveAfterCall);
        }
        CleanedAluExpansion? cleaned = CleanWithinBudget(expansion, call, liveAfterCall);
        if (cleaned?.Value is null) {
            return null;
        }
        return cleaned.Statements switch {
            [] => [assignment with { Right = cleaned.Value }],
            [BinaryOperationNode { Left: CpuFlagNode } flag] => [flag, assignment with { Right = cleaned.Value }],
            [VariableDeclarationNode result, BinaryOperationNode { Left: CpuFlagNode } flag] =>
                ReuseDestination(assignment, result, flag, expansion.Result.VariableName),
            _ => null
        };
    }

    /// <summary>
    /// The cleaned call wrapped by a type conversion or a declaration: only the forms that do not read the result
    /// back are accepted, because the destination is not a register of the result's type.
    /// </summary>
    private static IReadOnlyList<IVisitableAstNode>? TryRewriteWrapped(AluCallValueNode call,
        StatusFlags liveAfterCall, Func<ValueNode, IVisitableAstNode> wrap) {
        AluExpansion? expansion = AluExpansionBuilder.TryExpand(call);
        if (expansion is null) {
            return null;
        }
        CleanedAluExpansion? cleaned = CleanWithinBudget(expansion, call, liveAfterCall);
        if (cleaned?.Value is null) {
            return null;
        }
        return cleaned.Statements switch {
            [] => [wrap(cleaned.Value)],
            [BinaryOperationNode { Left: CpuFlagNode } flag] when ReadsOnlyOperands(flag.Right, expansion) =>
                [flag, wrap(cleaned.Value)],
            _ => null
        };
    }

    private static CleanedAluExpansion? CleanWithinBudget(AluExpansion expansion, AluCallValueNode call,
        StatusFlags liveAfterCall) {
        StatusFlags written = expansion.FlagAssignments.Aggregate(StatusFlags.None, (all, flag) => all | flag.Flag);
        StatusFlags liveWritten = written & liveAfterCall;
        if (BitOperations.PopCount((uint)liveWritten) > MaxLiveFlags
            || (liveWritten & ~ReadableFlags(call.Operation)) != StatusFlags.None) {
            return null;
        }
        return AluExpansionCleanup.Clean(expansion, liveAfterCall, true);
    }

    private static IReadOnlyList<IVisitableAstNode>? TryRewriteShiftValue(BinaryOperationNode assignment,
        AluCallValueNode call, StatusFlags liveAfterCall) {
        ValueNode? value = AluExpansionBuilder.TryExpandShiftValue(call);
        if (value is null || (FlagEffectVisitor.Of(call).MayWrite & liveAfterCall) != StatusFlags.None) {
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

    private static IReadOnlyList<IVisitableAstNode>? ReuseDestination(BinaryOperationNode assignment,
        VariableDeclarationNode result, BinaryOperationNode flag, string resultName) {
        if (result.VariableName != resultName || assignment.Left is not RegisterNode destination
            || destination.DataType != result.DataType || !ReadsOnly(flag.Right, result.Reference)) {
            return null;
        }
        BinaryOperationNode flagFromDestination = flag with { Right = Replace(flag.Right, result.Reference, destination) };
        return [assignment with { Right = result.Initializer }, flagFromDestination];
    }

    private static bool ReadsOnly(ValueNode node, VariableReferenceNode result) => node switch {
        VariableReferenceNode reference => reference == result,
        ConstantNode => true,
        TypeConversionNode or BinaryOperationNode =>
            IrAstChildren.ChildrenOf(node).OfType<ValueNode>().All(child => ReadsOnly(child, result)),
        _ => false
    };

    private static ValueNode Replace(ValueNode node, VariableReferenceNode result, ValueNode destination) => node switch {
        VariableReferenceNode reference when reference == result => destination,
        TypeConversionNode or BinaryOperationNode => (ValueNode)IrAstChildren.MapChildren(node,
            child => child is ValueNode value ? Replace(value, result, destination) : child),
        _ => node
    };

    /// <summary>
    /// Whether <paramref name="node"/> reads none of the expansion's temps: after cleaning, a flag assignment that
    /// is the only remaining statement reads only what the call arguments became.
    /// </summary>
    private static bool ReadsOnlyOperands(ValueNode node, AluExpansion expansion) {
        HashSet<string> temps = expansion.Operands.Select(operand => operand.VariableName)
            .Append(expansion.Result.VariableName).ToHashSet();
        return ReadsNoTemp(node, temps);
    }

    private static bool ReadsNoTemp(ValueNode node, HashSet<string> temps) => node switch {
        VariableReferenceNode reference => !temps.Contains(reference.VariableName),
        ConstantNode or RegisterNode or CpuFlagNode => true,
        TypeConversionNode or UnaryOperationNode or BinaryOperationNode or AbsolutePointerNode
            or SegmentedPointerNode =>
            IrAstChildren.ChildrenOf(node).OfType<ValueNode>().All(child => ReadsNoTemp(child, temps)),
        _ => false
    };

    private static IReadOnlyList<IVisitableAstNode>? TryRemoveUnusedCall(AluCallValueNode call,
        StatusFlags liveAfterCall) {
        AluExpansion? expansion = AluExpansionBuilder.TryExpand(call);
        if (expansion is null) {
            return null;
        }
        CleanedAluExpansion cleaned = AluExpansionCleanup.Clean(expansion, liveAfterCall, false);
        if (cleaned.Statements.Count != 0) {
            return null;
        }
        return [];
    }
}
