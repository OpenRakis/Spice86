namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Memory;

/// <summary>
/// One read of the stack (a pop, a peek, an <c>SS:[SP + displacement]</c> pointer read): the
/// statement, the delta below the current base it read, and the set of slot definitions that may
/// have supplied the value. <see cref="Complete"/> is false when the read could not be fully
/// attributed - unknown offset, a slot whose writers are gone, or a slot defined on some paths
/// only (the union then misses the paths where no push wrote the slot).
/// </summary>
internal sealed record IrStackSlotRead(IrStatement Statement, int Delta,
    IReadOnlySet<IrStackSlotDefinition> Definitions, bool Complete);
