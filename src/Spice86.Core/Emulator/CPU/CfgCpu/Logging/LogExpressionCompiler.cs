namespace Spice86.Core.Emulator.CPU.CfgCpu.Logging;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor.Expressions;
using Spice86.Core.Emulator.Memory;

using System.Linq.Expressions;

/// <summary>
/// Compiles named log expressions of the form "name=expression" into delegates.
/// </summary>
public class LogExpressionCompiler {
    private readonly State _state;
    private readonly Memory _memory;

    public LogExpressionCompiler(State state, IMemory memory) {
        _state = state;
        _memory = memory as Memory
            ?? throw new ArgumentException("Memory must be an instance of Memory", nameof(memory));
    }

    /// <summary>
    /// Parses and compiles a "name=expression" string into a <see cref="CompiledLogExpression"/>.
    /// </summary>
    /// <param name="namedExpression">A string of the form "name=expression".</param>
    /// <returns>A compiled log expression ready for evaluation.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="namedExpression"/> does not contain '=' or has an empty name.
    /// </exception>
    public CompiledLogExpression Compile(string namedExpression) {
        int separatorIndex = namedExpression.IndexOf('=');
        if (separatorIndex <= 0) {
            throw new ArgumentException(
                $"Expression must be in 'name=expression' format, got: '{namedExpression}'",
                nameof(namedExpression));
        }
        string name = namedExpression.Substring(0, separatorIndex);
        string expressionText = namedExpression.Substring(separatorIndex + 1);

        AstExpressionBuilder builder = new();
        Expression expressionTree = AstExpressionCompilationHelper.BuildExpression(expressionText, builder);
        Expression<Func<State, Memory, uint>> lambda = builder.ToFuncUInt32(expressionTree);
        Func<State, Memory, uint> compiledFunc = lambda.Compile();

        return new CompiledLogExpression(name, () => compiledFunc(_state, _memory));
    }
}
