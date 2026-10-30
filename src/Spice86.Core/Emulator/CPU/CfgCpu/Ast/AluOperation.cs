namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast;

/// <summary>The operation of an <c>Alu8</c> / <c>Alu16</c> / <c>Alu32</c> call.</summary>
public enum AluOperation {
    /// <summary>Addition.</summary>
    Add,
    /// <summary>Subtraction; also the shape of a compare.</summary>
    Sub,
    /// <summary>Bitwise and; also the shape of a test.</summary>
    And,
    /// <summary>Bitwise or.</summary>
    Or,
    /// <summary>Bitwise exclusive or.</summary>
    Xor,
    /// <summary>Increment by one.</summary>
    Inc,
    /// <summary>Decrement by one.</summary>
    Dec,
    /// <summary>Addition with carry.</summary>
    Adc,
    /// <summary>Subtraction with borrow.</summary>
    Sbb,
    /// <summary>Left shift.</summary>
    Shl,
    /// <summary>Logical right shift.</summary>
    Shr,
    /// <summary>Arithmetic right shift.</summary>
    Sar,
    /// <summary>Left rotate.</summary>
    Rol,
    /// <summary>Right rotate.</summary>
    Ror,
    /// <summary>Left rotate through carry.</summary>
    Rcl,
    /// <summary>Right rotate through carry.</summary>
    Rcr,
    /// <summary>Double-precision left shift.</summary>
    Shld,
    /// <summary>Double-precision right shift.</summary>
    Shrd,
    /// <summary>Unsigned multiply.</summary>
    Mul,
    /// <summary>Signed multiply.</summary>
    Imul,
    /// <summary>Unsigned divide.</summary>
    Div,
    /// <summary>Signed divide.</summary>
    Idiv,
    /// <summary>The flag update the ALU emits after an operation it lowered away.</summary>
    UpdateFlags
}
