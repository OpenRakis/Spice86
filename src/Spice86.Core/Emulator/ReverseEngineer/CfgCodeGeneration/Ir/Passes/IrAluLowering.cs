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
            if (AluAssignmentRewrite.TryRewriteStatement(untyped, liveAfterCall) is { } nodes) {
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

    /// <summary>The statement with its top-level typed ALU wrappers removed, for the shared match.</summary>
    internal static IVisitableAstNode Untype(IVisitableAstNode statement) => statement switch {
        AluStatementNode alu => alu.Call,
        AluOperationNode alu => AluCall(alu),
        BinaryOperationNode { BinaryOperation: BinaryOperation.ASSIGN, Right: AluOperationNode alu }
            assignment => assignment with { Right = AluCall(alu) },
        BinaryOperationNode { BinaryOperation: BinaryOperation.ASSIGN,
                Right: TypeConversionNode { Value: AluOperationNode alu } conversion }
            assignment => assignment with { Right = conversion with { Value = AluCall(alu) } },
        VariableDeclarationNode { Initializer: AluOperationNode alu } declaration =>
            declaration with { Initializer = AluCall(alu) },
        _ => statement
    };

    /// <summary>The call as an <see cref="AluCallValueNode"/>: a pass may have rebuilt the wrapped call as a
    /// plain <see cref="MethodCallValueNode"/>, which loses the fields the shared rewrite matches on.</summary>
    private static MethodCallValueNode AluCall(AluOperationNode alu) =>
        alu.Call is AluCallValueNode typed ? typed
            : new AluCallValueNode(alu.Call.DataType, alu.Operation, alu.Width,
                [.. alu.Call.CallNode.Arguments]);
}
