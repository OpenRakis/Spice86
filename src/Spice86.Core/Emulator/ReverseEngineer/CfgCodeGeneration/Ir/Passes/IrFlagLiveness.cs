namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Passes;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;

/// <summary>
/// The status flags' liveness in the SSA of a lifted method. <see cref="IrSsa.LiveUses"/> already
/// filters the dead uses: the use a phi makes of an operand is dead when the phi's result reaches no
/// statement or terminator, and the use a partial definition makes of the previous version is dead
/// when the new version reaches no use. What remains is what a flag write can still influence.
/// </summary>
internal static class IrFlagLiveness {
    /// <summary>The six status flags and their SSA values.</summary>
    public static readonly (StatusFlags Flag, IrSsaValue Value)[] StatusFlagValues = [
        (StatusFlags.Carry, IrSsaValue.Carry),
        (StatusFlags.Parity, IrSsaValue.Parity),
        (StatusFlags.Auxiliary, IrSsaValue.Auxiliary),
        (StatusFlags.Zero, IrSsaValue.Zero),
        (StatusFlags.Sign, IrSsaValue.Sign),
        (StatusFlags.Overflow, IrSsaValue.Overflow)
    ];

    /// <summary>
    /// The status flags still observed after the statement at <paramref name="index"/> in
    /// <paramref name="block"/>: a flag is live when the SSA version defined there reaches a use.
    /// </summary>
    public static StatusFlags LiveAfter(IrSsa ssa, IrBlock block, int index) {
        StatusFlags live = StatusFlags.None;
        foreach ((StatusFlags flag, IrSsaValue value) in StatusFlagValues) {
            int version = ssa.VersionAfter(block, index, value);
            if (version >= 0 && ssa.IsLive(version)) {
                live |= flag;
            }
        }
        return live;
    }

    /// <summary>Whether <paramref name="value"/> is one of the six status flags.</summary>
    public static bool IsStatusFlagValue(IrSsaValue value) =>
        value is >= IrSsaValue.Carry and <= IrSsaValue.Overflow;
}
