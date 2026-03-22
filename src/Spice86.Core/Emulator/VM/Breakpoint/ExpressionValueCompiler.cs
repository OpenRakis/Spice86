namespace Spice86.Core.Emulator.VM.Breakpoint;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Parser;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor.Expressions;
using Spice86.Core.Emulator.Memory;
using System.Linq.Expressions;

/// <summary>
/// Compiles expressions into value-returning functions using Expression trees.
/// This is the value-evaluation counterpart to <see cref="BreakpointConditionCompiler"/>:
/// same parser and builder infrastructure, but returns numeric values instead of booleans.
/// </summary>
public class ExpressionValueCompiler {
    private readonly State _state;
    private readonly Memory _memory;

    public ExpressionValueCompiler(State state, IMemory memory) {
        _state = state;
        _memory = memory as Memory ?? throw new ArgumentException("Memory must be an instance of Memory class", nameof(memory));
    }

    /// <summary>
    /// Compiles an expression string into a function that returns its value as a long.
    /// Reuses the same <see cref="AstExpressionParser"/> and <see cref="AstExpressionBuilder"/>
    /// as breakpoint conditions.
    /// </summary>
    /// <param name="expression">The expression (e.g., "ax", "word ptr ds:[bx + 0x10]").</param>
    /// <returns>A function that evaluates the expression and returns the value.</returns>
    public Func<long> Compile(string expression) {
        AstExpressionParser parser = new();
        ValueNode astNode = parser.Parse(expression);

        AstExpressionBuilder builder = new();
        Expression expressionTree = astNode.Accept(builder);
        Expression<Func<State, Memory, long>> lambda = builder.ToFuncLong(expressionTree);
        Func<State, Memory, long> compiledFunc = lambda.Compile();

        return () => compiledFunc(_state, _memory);
    }
}
