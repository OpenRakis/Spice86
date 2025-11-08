namespace Spice86.Shared.Emulator.VM.Breakpoint.Expression;

/// <summary>
/// Enumeration of supported unary operators.
/// </summary>
public enum UnaryOperator {
    /// <summary>Negation operator (-)</summary>
    Negate,
    /// <summary>Logical NOT operator (!)</summary>
    Not,
    /// <summary>Bitwise NOT operator (~)</summary>
    BitwiseNot
}
