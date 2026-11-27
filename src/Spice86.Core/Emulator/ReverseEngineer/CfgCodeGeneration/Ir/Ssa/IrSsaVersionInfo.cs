namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;

/// <summary>
/// What one SSA version is: <see cref="Block"/> is the block of the definition; <see cref="Previous"/>
/// is the version a partial definition also depends on, else -1. The position of the defining site is
/// derived from the current block list where it is needed; it is not stored because edits move it.
/// </summary>
internal sealed record IrSsaVersionInfo(
    IrSsaValue Value,
    IrSsaDefinitionKind Kind,
    IrBlock Block,
    int Previous);
