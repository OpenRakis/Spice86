namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Passes;

/// <summary>The register-pair operations <see cref="IrDoublePrecision"/> recognizes: two halves of
/// one wide operation linked by the carry.</summary>
internal enum DoublePrecisionForm {
    /// <summary><c>add</c> on the low halves then <c>adc</c> on the high halves.</summary>
    Add,
    /// <summary><c>sub</c> on the low halves then <c>sbb</c> on the high halves.</summary>
    Subtract,
    /// <summary><c>neg</c> on the low half, <c>adc</c> 0 on the high half, <c>neg</c> on it.</summary>
    Negate,
    /// <summary><c>shl</c> 1 on the low half then <c>rcl</c> 1 on the high half.</summary>
    ShiftLeft,
    /// <summary><c>shr</c> 1 on the high half then <c>rcr</c> 1 on the low half.</summary>
    ShiftRight,
    /// <summary><c>sar</c> 1 on the high half then <c>rcr</c> 1 on the low half: the wide
    /// arithmetic shift right.</summary>
    SignedShiftRight,
    /// <summary><c>and</c> on the low halves then <c>and</c> on the high halves.</summary>
    And,
    /// <summary><c>or</c> on the low halves then <c>or</c> on the high halves.</summary>
    Or,
    /// <summary><c>xor</c> on the low halves then <c>xor</c> on the high halves.</summary>
    Xor,
    /// <summary><c>not</c> on the low half then <c>not</c> on the high half.</summary>
    Not,
    /// <summary>The compiler's 32-bit product of two 16-bit pairs: one unsigned
    /// <c>mul</c> and two cross <c>imul</c> low halves accumulated into the high word.</summary>
    Multiply
}
