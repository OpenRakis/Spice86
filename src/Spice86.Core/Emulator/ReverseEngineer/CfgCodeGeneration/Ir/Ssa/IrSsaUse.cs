namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;

/// <summary>
/// One use of an SSA version: either a statement of <see cref="Block"/> at <see cref="Index"/>, the
/// terminator of <see cref="Block"/> (<see cref="Index"/> equals the block's statement count and
/// <see cref="Terminator"/> is set), or an operand of <see cref="Phi"/> reached through
/// <see cref="OperandPredecessor"/> (the phi's own block for a method entry operand).
/// </summary>
internal sealed record IrSsaUse(
    IrBlock Block,
    int Index,
    IrStatement? Statement,
    IrTerminator? Terminator,
    IrSsaPhi? Phi,
    IrBlock? OperandPredecessor) {

    /// <summary>
    /// The join version this use only feeds, or -1: when a statement partially defines a value it does
    /// not otherwise read, its use of the previous version exists only so long as the new version does.
    /// </summary>
    internal int ConditionalNewVersion { get; init; } = -1;
}
