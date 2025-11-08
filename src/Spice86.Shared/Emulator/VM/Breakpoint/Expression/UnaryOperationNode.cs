namespace Spice86.Shared.Emulator.VM.Breakpoint.Expression;

/// <summary>
/// Represents a unary operation in an expression.
/// </summary>
public class UnaryOperationNode : IExpressionNode {
    /// <summary>
    /// The operator.
    /// </summary>
    public UnaryOperator Operator { get; }
    
    /// <summary>
    /// The operand.
    /// </summary>
    public IExpressionNode Operand { get; }
    
    /// <summary>
    /// Initializes a new instance of the <see cref="UnaryOperationNode"/> class.
    /// </summary>
    /// <param name="op">The operator.</param>
    /// <param name="operand">The operand.</param>
    public UnaryOperationNode(UnaryOperator op, IExpressionNode operand) {
        Operator = op;
        Operand = operand;
    }
    
    /// <inheritdoc/>
    public long Evaluate(IExpressionContext context) {
        long value = Operand.Evaluate(context);
        
        return Operator switch {
            UnaryOperator.Negate => -value,
            UnaryOperator.Not => value == 0 ? 1 : 0,
            UnaryOperator.BitwiseNot => ~value,
            _ => throw new InvalidOperationException($"Unknown operator: {Operator}")
        };
    }
    
    /// <inheritdoc/>
    public override string ToString() {
        string opStr = Operator switch {
            UnaryOperator.Negate => "-",
            UnaryOperator.Not => "!",
            UnaryOperator.BitwiseNot => "~",
            _ => "?"
        };
        return $"{opStr}{Operand}";
    }
}
