namespace Spice86.Shared.Emulator.VM.Breakpoint.Expression;

/// <summary>
/// Represents a variable reference in an expression.
/// </summary>
public class VariableNode : IExpressionNode {
    /// <summary>
    /// The name of the variable.
    /// </summary>
    public string VariableName { get; }
    
    /// <summary>
    /// Initializes a new instance of the <see cref="VariableNode"/> class.
    /// </summary>
    /// <param name="variableName">The name of the variable.</param>
    public VariableNode(string variableName) {
        VariableName = variableName;
    }
    
    /// <inheritdoc/>
    public long Evaluate(IExpressionContext context) => context.GetVariable(VariableName);
    
    /// <inheritdoc/>
    public override string ToString() => VariableName;
}
