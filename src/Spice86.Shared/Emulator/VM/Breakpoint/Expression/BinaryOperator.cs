namespace Spice86.Shared.Emulator.VM.Breakpoint.Expression;

/// <summary>
/// Enumeration of supported binary operators.
/// </summary>
public enum BinaryOperator {
    /// <summary>Addition operator (+)</summary>
    Add,
    /// <summary>Subtraction operator (-)</summary>
    Subtract,
    /// <summary>Multiplication operator (*)</summary>
    Multiply,
    /// <summary>Division operator (/)</summary>
    Divide,
    /// <summary>Modulo operator (%)</summary>
    Modulo,
    /// <summary>Equality operator (==)</summary>
    Equal,
    /// <summary>Inequality operator (!=)</summary>
    NotEqual,
    /// <summary>Less than operator (&lt;)</summary>
    LessThan,
    /// <summary>Less than or equal operator (&lt;=)</summary>
    LessThanOrEqual,
    /// <summary>Greater than operator (&gt;)</summary>
    GreaterThan,
    /// <summary>Greater than or equal operator (&gt;=)</summary>
    GreaterThanOrEqual,
    /// <summary>Logical AND operator (&amp;&amp;)</summary>
    And,
    /// <summary>Logical OR operator (||)</summary>
    Or,
    /// <summary>Bitwise AND operator (&amp;)</summary>
    BitwiseAnd,
    /// <summary>Bitwise OR operator (|)</summary>
    BitwiseOr,
    /// <summary>Bitwise XOR operator (^)</summary>
    BitwiseXor,
    /// <summary>Left shift operator (&lt;&lt;)</summary>
    LeftShift,
    /// <summary>Right shift operator (&gt;&gt;)</summary>
    RightShift
}
