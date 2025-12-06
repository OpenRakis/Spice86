namespace Spice86.ViewModels.Services;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Parser;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM.Breakpoint;

/// <summary>
/// Service for compiling breakpoint condition expressions.
/// Provides shared logic for compiling and validating condition expressions
/// across different view models.
/// </summary>
public class BreakpointConditionService {
    private readonly State _state;
    private readonly IMemory _memory;

    /// <summary>
    /// Initializes a new instance of the <see cref="BreakpointConditionService"/> class.
    /// </summary>
    /// <param name="state">The CPU state for register access.</param>
    /// <param name="memory">The memory for memory access.</param>
    public BreakpointConditionService(State state, IMemory memory) {
        _state = state;
        _memory = memory;
    }

    /// <summary>
    /// Result of a condition compilation attempt.
    /// </summary>
    public record ConditionCompilationResult(
        bool Success,
        Func<long, bool>? Condition,
        string? ValidatedExpression,
        Exception? Error);

    /// <summary>
    /// Attempts to compile a condition expression.
    /// </summary>
    /// <param name="expression">The condition expression to compile.</param>
    /// <returns>A result containing the compilation outcome.</returns>
    public ConditionCompilationResult TryCompile(string? expression) {
        if (string.IsNullOrWhiteSpace(expression)) {
            return new ConditionCompilationResult(true, null, expression, null);
        }

        try {
            BreakpointConditionCompiler compiler = new(_state, _memory);
            Func<long, bool> condition = compiler.Compile(expression);
            return new ConditionCompilationResult(true, condition, expression, null);
        } catch (ExpressionParseException ex) {
            return new ConditionCompilationResult(false, null, null, ex);
        } catch (ArgumentException ex) {
            return new ConditionCompilationResult(false, null, null, ex);
        } catch (InvalidOperationException ex) {
            return new ConditionCompilationResult(false, null, null, ex);
        }
    }
}