namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Passes;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Propagation;

using System.Linq;

/// <summary>
/// For each instruction that cannot fault and has no
/// after-check, the first body statement - an unused ALU call, an assignment, an assignment through a
/// type conversion, or a declaration over an ALU call - is rewritten with the flag liveness read off the
/// SSA right after the call. The typed wrappers the lifter puts on ALU calls
/// (<see cref="AluStatementNode"/>, <see cref="AluOperationNode"/>) are unwrapped for the match; the
/// shared rewrite produces plain AST, so the replacements need no re-typing.
/// </summary>
internal static class IrAluLowering {
    private static readonly IReadOnlyDictionary<string, ConstantNode> EmptyConstants =
        new Dictionary<string, ConstantNode>();

    /// <summary>Rewrites the first body statement of every rewritable instruction.</summary>
    public static void Apply(IrMethod method, IrSsa ssa) {
        List<(IrBlock Block, int Index, IReadOnlyList<IVisitableAstNode> Nodes, ICfgNode Origin)> edits = [];
        foreach ((ICfgNode origin, List<IrOrigins.Located> statements) in IrOrigins.ByOrigin(method)) {
            if (!IrOrigins.Rewritable(statements, origin)
                || IrOrigins.FirstBodyStatement(statements, origin) is not { } first) {
                continue;
            }
            IVisitableAstNode untyped = Untype(first.Statement.Node);
            StatusFlags liveAfterCall = IrFlagLiveness.LiveAfter(ssa, first.Block, first.Index);
            if ((TryConstantAlu(untyped, first.Statement.Effects, ssa, first.Block, first.Index)
                    ?? AluAssignmentRewrite.TryRewriteStatement(untyped, origin, liveAfterCall))
                is { } nodes) {
                edits.Add((first.Block, first.Index, nodes, origin));
            }
        }
        foreach ((IrBlock block, int index, IReadOnlyList<IVisitableAstNode> nodes, ICfgNode origin)
            in edits.OrderByDescending(edit => edit.Index)) {
            IrStatement old = block.Statements[index];
            block.Statements.RemoveAt(index);
            IrStatement[] replacements = nodes
                .Select(node => IrStatement.Create(IrConstantFolder.Fold(node, EmptyConstants), origin))
                .ToArray();
            for (int i = replacements.Length - 1; i >= 0; i--) {
                block.Statements.Insert(index, replacements[i]);
            }
            if (replacements.Length == 0) {
                ssa.Remove(old);
            } else {
                ssa.Replace(old, replacements);
            }
        }
    }

    /// <summary>
    /// The replacement statements when every operand of an ALU call is a constant: the constant
    /// value assignment and one flag write per status flag the emulator's ALU writes for these
    /// operands (<see cref="ConstantEvaluation.TryEvaluateAluFlags"/>), or <c>null</c>. The
    /// carry-reading operations fold only when the carry version before the statement is a constant
    /// flag write.
    /// </summary>
    private static IReadOnlyList<IVisitableAstNode>? TryConstantAlu(IVisitableAstNode statement,
        IrEffects effects, IrSsa ssa, IrBlock block, int index) {
        AluCallValueNode? call;
        Func<ulong, IVisitableAstNode>? write;
        switch (statement) {
            case AluCallValueNode standalone:
                call = standalone;
                write = null;
                break;
            case BinaryOperationNode { BinaryOperation: BinaryOperation.ASSIGN,
                    Right: AluCallValueNode right } assignment:
                call = right;
                write = value => assignment with { Right = Constant(right.DataType, value) };
                break;
            case BinaryOperationNode { BinaryOperation: BinaryOperation.ASSIGN,
                    Right: TypeConversionNode { Value: AluCallValueNode right } conversion }
                assignment:
                call = right;
                write = value => assignment with {
                    Right = conversion with { Value = Constant(right.DataType, value) }
                };
                break;
            case VariableDeclarationNode { Initializer: AluCallValueNode right } declaration:
                call = right;
                write = value => declaration with {
                    Initializer = Constant(right.DataType, value)
                };
                break;
            default:
                return null;
        }
        if (!ConstantEvaluation.TryEvaluateAluFlags(call,
                leaf => ConstantEvaluation.Resolved(leaf)?.Value,
                CarryBefore(ssa, block, index), out ulong value,
                out List<(StatusFlags Flag, bool Set)> flags)) {
            return null;
        }
        // The written flags are exactly the flag bits the statement must write: an unwritten may-only
        // flag would keep a version no new statement fully defines, one extra would define a value it
        // did not, and a partial flag definition cannot be replaced by a full flag write.
        CpuValues writtenFlags = effects.MayWrite & CpuValues.StatusFlags;
        StatusFlags emitted = flags.Aggregate(StatusFlags.None,
            (all, flag) => all | flag.Flag);
        if (IrValueSets.FromStatusFlags(emitted) != writtenFlags
            || writtenFlags != (effects.MustWrite & CpuValues.StatusFlags)) {
            return null;
        }
        List<IVisitableAstNode> nodes = [];
        if (write is not null) {
            nodes.Add(write(value));
        }
        nodes.AddRange(flags.Select(flag => new AluFlagAssignment(flag.Flag,
            new ConstantNode(DataType.BOOL, flag.Set ? 1UL : 0UL)).ToAssignment()));
        return nodes;
    }

    /// <summary>The carry flag before the statement at <paramref name="index"/> when its version is
    /// a constant flag write, or <c>null</c>: anything else (method entry, an ALU call, a
    /// flag-register restore) leaves it unknown.</summary>
    private static bool? CarryBefore(IrSsa ssa, IrBlock block, int index) {
        int version = ssa.VersionBefore(block, index, IrSsaValue.Carry);
        return ssa.DefiningStatement(version)?.Node is BinaryOperationNode {
            BinaryOperation: BinaryOperation.ASSIGN,
            Left: CpuFlagNode { FlagMask: Flags.Carry },
            Right: ConstantNode carry } ? carry.Value != 0 : null;
    }

    private static ConstantNode Constant(DataType type, ulong value) =>
        new(type, ConstantEvaluation.Bits(type, value));

    /// <summary>The statement with its top-level typed ALU wrappers removed, for the shared match.</summary>
    internal static IVisitableAstNode Untype(IVisitableAstNode statement) => statement switch {
        AluStatementNode alu => alu.Call,
        AluOperationNode alu => alu.Call,
        BinaryOperationNode { BinaryOperation: BinaryOperation.ASSIGN, Right: AluOperationNode alu }
            assignment => assignment with { Right = alu.Call },
        BinaryOperationNode { BinaryOperation: BinaryOperation.ASSIGN,
                Right: TypeConversionNode { Value: AluOperationNode alu } conversion }
            assignment => assignment with { Right = conversion with { Value = alu.Call } },
        VariableDeclarationNode { Initializer: AluOperationNode alu } declaration =>
            declaration with { Initializer = alu.Call },
        _ => statement
    };
}
