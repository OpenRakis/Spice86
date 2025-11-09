namespace Spice86.Core.Emulator.VM.Breakpoint;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;

/// <summary>
/// Compiles breakpoint condition expressions into executable functions.
/// This class is shared across different parts of the codebase (GDB, UI, serialization)
/// to provide consistent condition evaluation.
/// </summary>
public class BreakpointConditionCompiler {
    private readonly State _state;
    private readonly IMemory _memory;

    public BreakpointConditionCompiler(State state, IMemory memory) {
        _state = state;
        _memory = memory;
    }

    /// <summary>
    /// Compiles a condition expression string into an executable function.
    /// </summary>
    /// <param name="expression">The condition expression (e.g., "ax == 0x100 &amp;&amp; byte[address] &gt; 0x42").</param>
    /// <returns>A compiled function that evaluates the condition given a trigger address.</returns>
    /// <exception cref="ArgumentException">Thrown if the expression cannot be parsed or compiled.</exception>
    public Func<long, bool> Compile(string expression) {
        // TODO: This will use the AstExpressionParser and AstExpressionBuilder
        // For now, use the old expression parser as a placeholder
        Shared.Emulator.VM.Breakpoint.Expression.ExpressionParser parser = new();
        Shared.Emulator.VM.Breakpoint.Expression.IExpressionNode ast = parser.Parse(expression);
        
        return (address) => {
            BreakpointExpressionContext context = new(_state, _memory, address);
            return ast.Evaluate(context) != 0;
        };
    }
}
