namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;

/// <summary>
/// A phi at the top of a block: one operand per in-method predecessor, in block order; when
/// <see cref="HasEntryOperand"/> the block is a method entry block whose entry version of the value
/// also merges into the result.
/// </summary>
internal sealed record IrSsaPhi(
    IrSsaValue Value,
    int Result,
    IReadOnlyList<(IrBlock Predecessor, int Version)> Operands,
    bool HasEntryOperand);
