namespace Spice86.Shared.Emulator.VM.Breakpoint.Expression;

/// <summary>
/// Represents a binary operation in an expression.
/// </summary>
public class BinaryOperationNode : IExpressionNode {
    /// <summary>
    /// The left operand.
    /// </summary>
    public IExpressionNode Left { get; }
    
    /// <summary>
    /// The operator.
    /// </summary>
    public BinaryOperator Operator { get; }
    
    /// <summary>
    /// The right operand.
    /// </summary>
    public IExpressionNode Right { get; }
    
    /// <summary>
    /// Initializes a new instance of the <see cref="BinaryOperationNode"/> class.
    /// </summary>
    /// <param name="left">The left operand.</param>
    /// <param name="op">The operator.</param>
    /// <param name="right">The right operand.</param>
    public BinaryOperationNode(IExpressionNode left, BinaryOperator op, IExpressionNode right) {
        Left = left;
        Operator = op;
        Right = right;
    }
    
    /// <inheritdoc/>
    public long Evaluate(IExpressionContext context) {
        long leftValue = Left.Evaluate(context);
        long rightValue = Right.Evaluate(context);
        
        return Operator switch {
            BinaryOperator.Add => leftValue + rightValue,
            BinaryOperator.Subtract => leftValue - rightValue,
            BinaryOperator.Multiply => leftValue * rightValue,
            BinaryOperator.Divide => rightValue != 0 ? leftValue / rightValue : 0,
            BinaryOperator.Modulo => rightValue != 0 ? leftValue % rightValue : 0,
            BinaryOperator.Equal => leftValue == rightValue ? 1 : 0,
            BinaryOperator.NotEqual => leftValue != rightValue ? 1 : 0,
            BinaryOperator.LessThan => leftValue < rightValue ? 1 : 0,
            BinaryOperator.LessThanOrEqual => leftValue <= rightValue ? 1 : 0,
            BinaryOperator.GreaterThan => leftValue > rightValue ? 1 : 0,
            BinaryOperator.GreaterThanOrEqual => leftValue >= rightValue ? 1 : 0,
            BinaryOperator.And => (leftValue != 0 && rightValue != 0) ? 1 : 0,
            BinaryOperator.Or => (leftValue != 0 || rightValue != 0) ? 1 : 0,
            BinaryOperator.BitwiseAnd => leftValue & rightValue,
            BinaryOperator.BitwiseOr => leftValue | rightValue,
            BinaryOperator.BitwiseXor => leftValue ^ rightValue,
            BinaryOperator.LeftShift => leftValue << (int)rightValue,
            BinaryOperator.RightShift => leftValue >> (int)rightValue,
            _ => throw new InvalidOperationException($"Unknown operator: {Operator}")
        };
    }
    
    /// <inheritdoc/>
    public override string ToString() {
        string opStr = Operator switch {
            BinaryOperator.Add => "+",
            BinaryOperator.Subtract => "-",
            BinaryOperator.Multiply => "*",
            BinaryOperator.Divide => "/",
            BinaryOperator.Modulo => "%",
            BinaryOperator.Equal => "==",
            BinaryOperator.NotEqual => "!=",
            BinaryOperator.LessThan => "<",
            BinaryOperator.LessThanOrEqual => "<=",
            BinaryOperator.GreaterThan => ">",
            BinaryOperator.GreaterThanOrEqual => ">=",
            BinaryOperator.And => "&&",
            BinaryOperator.Or => "||",
            BinaryOperator.BitwiseAnd => "&",
            BinaryOperator.BitwiseOr => "|",
            BinaryOperator.BitwiseXor => "^",
            BinaryOperator.LeftShift => "<<",
            BinaryOperator.RightShift => ">>",
            _ => "?"
        };
        return $"({Left} {opStr} {Right})";
    }
}
