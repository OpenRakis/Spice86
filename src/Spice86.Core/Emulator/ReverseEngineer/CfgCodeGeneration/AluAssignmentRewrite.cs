namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;

using System.Linq;
using System.Numerics;

/// <summary>
/// Rewrites an instruction whose only work is one ALU call (<c>add</c>, <c>sub</c>, <c>and</c>, <c>or</c>, <c>xor</c>,
/// <c>inc</c>, <c>dec</c>, <c>shl</c>, <c>shr</c>, <c>sar</c>, <c>rol</c>, <c>ror</c>, <c>rcl</c>, <c>rcr</c>,
/// <c>adc</c>, <c>sbb</c>, and <c>cmp</c> / <c>test</c> whose value is unused) as
/// plain code: an assignment becomes plain arithmetic when at most one flag the call writes is live after it and that
/// flag has a readable form; a shift by a count that is not a constant becomes the masked shift when no flag it may
/// write is live after it; an unused call disappears when no flag it writes is live after it and its operands read no
/// memory and call nothing.
/// </summary>
internal static class AluAssignmentRewrite {
    private const int MaxLiveFlags = 1;

    /// <summary>
    /// The rewritten AST for an AST <c>{ destination = AluN.Op(...); MoveIpNext; }</c>, or <c>{ MoveIpNext; }</c>
    /// when the ALU call writes no live flag and its operands read no memory or call, or <c>null</c>.
    /// </summary>
    /// <param name="node">The execution AST of the instruction.</param>
    /// <param name="flagsLiveAfter">The status flags live after the instruction.</param>
    public static BlockNode? TryRewrite(IVisitableAstNode node, StatusFlags flagsLiveAfter) {
        if (node is BlockNode { Statements: [MethodCallValueNode unusedCall, MoveIpNextNode ipMove] }) {
            return TryRemoveUnusedCall(unusedCall, ipMove, flagsLiveAfter);
        }
        if (node is not BlockNode {
                Statements: [
                    BinaryOperationNode { BinaryOperation: BinaryOperation.ASSIGN, Right: MethodCallValueNode call } assignment,
                    MoveIpNextNode moveIp
                ]
            }) {
            return null;
        }
        AluExpansion? expansion = AluExpansionBuilder.TryExpand(call);
        if (expansion is null) {
            return TryRewriteShiftValue(assignment, call, moveIp, flagsLiveAfter);
        }
        StatusFlags written = expansion.FlagAssignments.Aggregate(StatusFlags.None, (all, flag) => all | flag.Flag);
        StatusFlags liveWritten = written & flagsLiveAfter;
        if (BitOperations.PopCount((uint)liveWritten) > MaxLiveFlags
            || (liveWritten & ~ReadableFlags(call.CallNode.MethodName)) != StatusFlags.None) {
            return null;
        }
        CleanedAluExpansion cleaned = AluExpansionCleanup.Clean(expansion, flagsLiveAfter, true);
        if (cleaned.Value is null) {
            return null;
        }
        return cleaned.Statements switch {
            [] => new BlockNode(assignment with { Right = cleaned.Value }, moveIp),
            [BinaryOperationNode { Left: CpuFlagNode } flag] =>
                new BlockNode(flag, assignment with { Right = cleaned.Value }, moveIp),
            [VariableDeclarationNode result, BinaryOperationNode { Left: CpuFlagNode } flag] =>
                ReuseDestination(assignment, result, flag, moveIp, expansion.Result.VariableName),
            _ => null
        };
    }

    private static BlockNode? TryRewriteShiftValue(BinaryOperationNode assignment, MethodCallValueNode call,
        MoveIpNextNode moveIp, StatusFlags flagsLiveAfter) {
        ValueNode? value = AluExpansionBuilder.TryExpandShiftValue(call);
        if (value is null || (FlagEffectVisitor.Of(call).MayWrite & flagsLiveAfter) != StatusFlags.None) {
            return null;
        }
        return new BlockNode(assignment with { Right = value }, moveIp);
    }

    private static StatusFlags ReadableFlags(string operation) => operation switch {
        "Add" => StatusFlags.Zero | StatusFlags.Sign,
        "Sub" => StatusFlags.Carry | StatusFlags.Zero | StatusFlags.Sign,
        "And" or "Or" or "Xor" => StatusFlags.Carry | StatusFlags.Auxiliary | StatusFlags.Zero | StatusFlags.Sign
            | StatusFlags.Overflow,
        "Inc" or "Dec" => StatusFlags.Zero | StatusFlags.Sign | StatusFlags.Overflow,
        "Shl" or "Shr" or "Sar" => StatusFlags.Carry | StatusFlags.Zero | StatusFlags.Sign,
        "Rol" or "Ror" => StatusFlags.Carry,
        _ => StatusFlags.None
    };

    private static BlockNode? ReuseDestination(BinaryOperationNode assignment, VariableDeclarationNode result,
        BinaryOperationNode flag, MoveIpNextNode moveIp, string resultName) {
        if (result.VariableName != resultName || assignment.Left is not RegisterNode destination
            || destination.DataType != result.DataType || !ReadsOnly(flag.Right, result.Reference)) {
            return null;
        }
        BinaryOperationNode flagFromDestination = flag with { Right = Replace(flag.Right, result.Reference, destination) };
        return new BlockNode(assignment with { Right = result.Initializer }, flagFromDestination, moveIp);
    }

    private static bool ReadsOnly(ValueNode node, VariableReferenceNode result) => node switch {
        VariableReferenceNode reference => reference == result,
        ConstantNode => true,
        TypeConversionNode conversion => ReadsOnly(conversion.Value, result),
        BinaryOperationNode binary => ReadsOnly(binary.Left, result) && ReadsOnly(binary.Right, result),
        _ => false
    };

    private static ValueNode Replace(ValueNode node, VariableReferenceNode result, ValueNode destination) => node switch {
        VariableReferenceNode reference when reference == result => destination,
        TypeConversionNode conversion => conversion with { Value = Replace(conversion.Value, result, destination) },
        BinaryOperationNode binary => binary with {
            Left = Replace(binary.Left, result, destination),
            Right = Replace(binary.Right, result, destination)
        },
        _ => node
    };

    private static BlockNode? TryRemoveUnusedCall(MethodCallValueNode call, MoveIpNextNode moveIp, StatusFlags flagsLiveAfter) {
        AluExpansion? expansion = AluExpansionBuilder.TryExpand(call);
        if (expansion is null) {
            return null;
        }
        CleanedAluExpansion cleaned = AluExpansionCleanup.Clean(expansion, flagsLiveAfter, false);
        if (cleaned.Statements.Count != 0) {
            return null;
        }
        return new BlockNode(moveIp);
    }
}
