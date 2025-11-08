namespace Spice86.Shared.Emulator.VM.Breakpoint.Expression;

/// <summary>
/// Represents a constant numeric value in an expression.
/// </summary>
public class ConstantNode : IExpressionNode {
    /// <summary>
    /// The constant value.
    /// </summary>
    public long Value { get; }
    
    /// <summary>
    /// Initializes a new instance of the <see cref="ConstantNode"/> class.
    /// </summary>
    /// <param name="value">The constant value.</param>
    public ConstantNode(long value) {
        Value = value;
    }
    
    /// <inheritdoc/>
    public long Evaluate(IExpressionContext context) => Value;
    
    /// <inheritdoc/>
    public override string ToString() => Value.ToString();
}
