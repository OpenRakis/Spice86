namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;

/// <summary>
/// What one SSA version is: <see cref="Block"/> is the block of the definition;
/// <see cref="StatementIndex"/> is the index of the defining statement in the block, the count of the
/// block's statements for a terminator definition, or -1 for a phi and a method entry version;
/// <see cref="Previous"/> is the version a partial definition also depends on, else -1.
/// </summary>
internal sealed record IrSsaVersionInfo(
    IrSsaValue Value,
    IrSsaDefinitionKind Kind,
    IrBlock Block,
    int StatementIndex,
    int Previous);
