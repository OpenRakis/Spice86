namespace Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor.Expressions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Parser;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;

using System.Linq.Expressions;

/// <summary>
/// Shared pipeline for parsing an expression string into a LINQ <see cref="Expression"/> tree.
/// Used by both <see cref="BreakpointConditionCompiler"/> and log-expression compilers so the
/// parse-and-build logic is not duplicated.
/// </summary>
public static class AstExpressionCompilationHelper {
    /// <summary>
    /// Parses <paramref name="expression"/> and converts it to a LINQ <see cref="Expression"/>
    /// via the <see cref="AstExpressionBuilder"/> visitor.
    /// </summary>
    /// <param name="expression">The expression text to parse.</param>
    /// <param name="builder">
    /// The builder instance that owns the <c>State</c> and <c>Memory</c> parameters the returned
    /// expression is bound to. Callers must use the same <paramref name="builder"/> instance when
    /// invoking any <c>ToFunc*</c> or <c>ToAction</c> method afterwards.
    /// </param>
    /// <returns>The untyped LINQ expression tree.</returns>
    public static Expression BuildExpression(string expression, AstExpressionBuilder builder) {
        AstExpressionParser parser = new();
        ValueNode astNode = parser.Parse(expression);
        return astNode.Accept(builder);
    }
}
