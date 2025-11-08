namespace Spice86.Shared.Emulator.VM.Breakpoint.Expression;

/// <summary>
/// Base interface for expression AST nodes used in conditional breakpoints.
/// </summary>
public interface IExpressionNode {
    /// <summary>
    /// Evaluates the expression node with the given context.
    /// </summary>
    /// <param name="context">The evaluation context containing variable values.</param>
    /// <returns>The result of evaluating the expression.</returns>
    long Evaluate(IExpressionContext context);
    
    /// <summary>
    /// Converts the expression node to its string representation.
    /// </summary>
    /// <returns>A string representation of the expression.</returns>
    string ToString();
}
