namespace Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor.Expressions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;

/// <summary>
/// Rewrites the execution AST of a <see cref="ICfgNode"/> before the JIT compiles it. The default
/// implementation is <see cref="IdentityExecutionAstRewriter"/>: the AST is compiled unchanged.
/// </summary>
public interface IExecutionAstRewriter {
    /// <summary>The AST to compile for <paramref name="node"/>, or <paramref name="executionAst"/> unchanged.</summary>
    IVisitableAstNode Rewrite(ICfgNode node, IVisitableAstNode executionAst);
}
