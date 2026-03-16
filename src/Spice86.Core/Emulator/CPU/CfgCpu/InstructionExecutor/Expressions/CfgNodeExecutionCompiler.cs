namespace Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor.Expressions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;

/// <summary>
/// Compiles the execution AST of a <see cref="ICfgNode"/> into a <see cref="CfgNodeExecutionAction{T}"/> delegate
/// and assigns it to <see cref="ICfgNode.CompiledExecution"/>. Must be called at init time, not on the hot path.
/// </summary>
public static class CfgNodeExecutionCompiler {
    /// <summary>
    /// Compiles and assigns <see cref="ICfgNode.CompiledExecution"/> for the given node.
    /// </summary>
    public static void Compile(ICfgNode node) {
        AstBuilder astBuilder = new();
        IVisitableAstNode executionAst = node.GenerateExecutionAst(astBuilder);
        AstExpressionBuilder expressionBuilder = new();
        CfgNodeExecutionAction<InstructionExecutionHelper> compiled =
            expressionBuilder.ToActionWithHelper(executionAst.Accept(expressionBuilder)).Compile();
#if DEBUG
        node.CompiledExecution = WrapWithDebugContext(node, compiled);
#else
        node.CompiledExecution = compiled;
#endif
    }

#if DEBUG
    /// <summary>
    /// Wraps a compiled delegate so that arithmetic exceptions carry the node's address and identity.
    /// The description string is captured once at compile time (init path), never on the hot path.
    /// </summary>
    private static CfgNodeExecutionAction<InstructionExecutionHelper> WrapWithDebugContext(
        ICfgNode node,
        CfgNodeExecutionAction<InstructionExecutionHelper> compiled) {
        string nodeDescription = $"node {node.Address} (Id={node.Id}, Type={node.GetType().Name})";
        return helper => {
            try {
                compiled(helper);
            } catch (DivideByZeroException ex) {
                throw new InvalidOperationException(
                    $"DivideByZeroException in compiled expression for {nodeDescription}", ex);
            } catch (OverflowException ex) {
                throw new InvalidOperationException(
                    $"OverflowException in compiled expression for {nodeDescription}", ex);
            }
        };
    }
#endif
}
