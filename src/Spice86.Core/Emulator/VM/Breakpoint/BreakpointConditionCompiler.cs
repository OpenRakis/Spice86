namespace Spice86.Core.Emulator.VM.Breakpoint;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor.Expressions;
using Spice86.Core.Emulator.Memory;
using System.Linq.Expressions;

/// <summary>
/// Compiles breakpoint condition expressions into executable functions using Expression trees.
/// This class is shared across different parts of the codebase (GDB, UI, serialization)
/// to provide consistent condition evaluation with high performance via compiled native code.
/// </summary>
public class BreakpointConditionCompiler {
    private readonly State _state;
    private readonly Memory _memory;

    public BreakpointConditionCompiler(State state, IMemory memory) {
        _state = state;
        // AstExpressionBuilder requires the concrete Memory type, not just IMemory
        _memory = memory as Memory ?? throw new ArgumentException("Memory must be an instance of Memory class", nameof(memory));
    }

    /// <summary>
    /// Compiles a condition expression string into an executable function using Expression trees.
    /// The resulting function is compiled to native code for high-performance evaluation.
    /// </summary>
    /// <param name="expression">The condition expression (e.g., "ax == 0x100 &amp;&amp; byte[address] &gt; 0x42").</param>
    /// <returns>A compiled function that evaluates the condition given a trigger address.</returns>
    /// <exception cref="ArgumentException">Thrown if the expression cannot be parsed or compiled.</exception>
    public Func<long, bool> Compile(string expression) {
        AstExpressionBuilder builder = new();
        Expression expressionTree = AstExpressionCompilationHelper.BuildExpression(expression, builder);
        Expression<Func<State, Memory, bool>> lambda = builder.ToFuncBool(expressionTree);
        Func<State, Memory, bool> compiledFunc = lambda.Compile();
        return (address) => compiledFunc(_state, _memory);
    }
}
