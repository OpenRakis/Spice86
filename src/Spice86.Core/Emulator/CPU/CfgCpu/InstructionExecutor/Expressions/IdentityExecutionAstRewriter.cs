namespace Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor.Expressions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;

/// <summary>The rewriter that leaves every execution AST unchanged.</summary>
public sealed class IdentityExecutionAstRewriter : IExecutionAstRewriter {
    /// <inheritdoc />
    public IVisitableAstNode Rewrite(ICfgNode node, IVisitableAstNode executionAst) => executionAst;
}
